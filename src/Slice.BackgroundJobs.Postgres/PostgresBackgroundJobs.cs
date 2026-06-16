using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using Slice.BackgroundJobs;
using Slice.Postgres;

namespace Slice.BackgroundJobs.Postgres;

internal static class PostgresJobsSchema
{
    public const int MaxRetries = 5;

    public const string Ddl = """
        CREATE TABLE IF NOT EXISTS slice_jobs (
            id           uuid PRIMARY KEY,
            job_type     text NOT NULL,
            args         jsonb NOT NULL,
            next_run_at  timestamptz NOT NULL,
            completed_at timestamptz NULL,
            retry_count  int NOT NULL DEFAULT 0,
            error        text NULL
        );
        CREATE INDEX IF NOT EXISTS ix_slice_jobs_due ON slice_jobs (next_run_at) WHERE completed_at IS NULL;

        CREATE TABLE IF NOT EXISTS slice_recurring_jobs (
            id           text PRIMARY KEY,
            job_type     text NOT NULL,
            args         jsonb NOT NULL,
            interval_ms  bigint NOT NULL,
            next_run_at  timestamptz NOT NULL
        );
        """;

    public static NpgsqlParameter Jsonb(string name, string json) => new(name, NpgsqlDbType.Jsonb) { Value = json };
}

/// <summary>Durable fire-and-forget jobs: each enqueue is a row in <c>slice_jobs</c>; the args type is
/// stored as its assembly-qualified name (like the EF outbox) so the worker can rehydrate it.</summary>
public sealed class PostgresBackgroundJobManager(NpgsqlDataSource dataSource) : IBackgroundJobManager
{
    public async Task<string> EnqueueAsync<TArgs>(TArgs args, TimeSpan? delay = null)
    {
        var id = Guid.CreateVersion7();
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO slice_jobs (id, job_type, args, next_run_at)
            VALUES (@id, @type, @args, now() + (@delay_ms || ' milliseconds')::interval)
            """;
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("type", typeof(TArgs).AssemblyQualifiedName!);
        cmd.Parameters.Add(PostgresJobsSchema.Jsonb("args", JsonSerializer.Serialize(args)));
        cmd.Parameters.AddWithValue("delay_ms", (long)(delay?.TotalMilliseconds ?? 0));
        await cmd.ExecuteNonQueryAsync();
        return id.ToString();
    }
}

public sealed class PostgresRecurringJobManager(NpgsqlDataSource dataSource) : IRecurringJobManager
{
    public void AddOrUpdate<TArgs>(string jobId, TArgs args, TimeSpan interval)
    {
        using var connection = dataSource.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO slice_recurring_jobs (id, job_type, args, interval_ms, next_run_at)
            VALUES (@id, @type, @args, @ms, now() + (@ms || ' milliseconds')::interval)
            ON CONFLICT (id) DO UPDATE
              SET job_type = EXCLUDED.job_type, args = EXCLUDED.args, interval_ms = EXCLUDED.interval_ms
            """;
        cmd.Parameters.AddWithValue("id", jobId);
        cmd.Parameters.AddWithValue("type", typeof(TArgs).AssemblyQualifiedName!);
        cmd.Parameters.Add(PostgresJobsSchema.Jsonb("args", JsonSerializer.Serialize(args)));
        cmd.Parameters.AddWithValue("ms", (long)interval.TotalMilliseconds);
        cmd.ExecuteNonQuery();
    }
}

/// <summary>Polls <c>slice_jobs</c> for due work (FOR UPDATE SKIP LOCKED), rehydrates the args type,
/// resolves <c>IBackgroundJob&lt;TArgs&gt;</c> and runs it; marks done or bumps retry on failure.</summary>
public sealed class PostgresJobWorker(
    NpgsqlDataSource dataSource, IServiceScopeFactory scopeFactory, ILogger<PostgresJobWorker> logger)
    : BackgroundService
{
    private const int BatchSize = 25;
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunDueAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { logger.LogError(ex, "Postgres job poll failed"); }

            try { await Task.Delay(Poll, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunDueAsync(CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var jobs = new List<(Guid Id, string Type, string Args)>();
        await using (var select = connection.CreateCommand())
        {
            select.CommandText = $"""
                SELECT id, job_type, args::text FROM slice_jobs
                WHERE completed_at IS NULL AND next_run_at <= now() AND retry_count < {PostgresJobsSchema.MaxRetries}
                ORDER BY next_run_at
                FOR UPDATE SKIP LOCKED
                LIMIT {BatchSize}
                """;
            await using var reader = await select.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                jobs.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        }

        if (jobs.Count == 0) { await tx.RollbackAsync(ct); return; }

        foreach (var (id, typeName, argsJson) in jobs)
        {
            try
            {
                await ExecuteJobAsync(typeName, argsJson, ct);
                await UpdateAsync(connection, "UPDATE slice_jobs SET completed_at = now() WHERE id = @id", id, null, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Postgres job {Id} failed", id);
                await UpdateAsync(connection,
                    "UPDATE slice_jobs SET retry_count = retry_count + 1, error = @err, next_run_at = now() + (power(2, retry_count) || ' seconds')::interval WHERE id = @id",
                    id, ex.Message, ct);
            }
        }

        await tx.CommitAsync(ct);
    }

    private async Task ExecuteJobAsync(string typeName, string argsJson, CancellationToken ct)
    {
        var argsType = Type.GetType(typeName) ?? throw new InvalidOperationException($"Unknown job args type '{typeName}'.");
        var args = JsonSerializer.Deserialize(argsJson, argsType);
        var jobInterface = typeof(IBackgroundJob<>).MakeGenericType(argsType);

        using var scope = scopeFactory.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService(jobInterface);
        var method = jobInterface.GetMethod(nameof(IBackgroundJob<object>.ExecuteAsync))!;
        await (Task)method.Invoke(handler, [args, ct])!;
    }

    private static async Task UpdateAsync(NpgsqlConnection connection, string sql, Guid id, string? error, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("id", id);
        if (sql.Contains("@err")) cmd.Parameters.AddWithValue("err", (object?)error ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

/// <summary>Enqueues due recurring jobs into <c>slice_jobs</c> and advances their next run time.</summary>
public sealed class PostgresRecurringScheduler(NpgsqlDataSource dataSource, ILogger<PostgresRecurringScheduler> logger)
    : BackgroundService
{
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { logger.LogError(ex, "Postgres recurring scheduler failed"); }

            try { await Task.Delay(Poll, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var due = new List<(string Id, string Type, string Args)>();
        await using (var select = connection.CreateCommand())
        {
            select.CommandText = """
                SELECT id, job_type, args::text FROM slice_recurring_jobs
                WHERE next_run_at <= now()
                FOR UPDATE SKIP LOCKED
                """;
            await using var reader = await select.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                due.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        if (due.Count == 0) { await tx.RollbackAsync(ct); return; }

        foreach (var (id, type, args) in due)
        {
            await using (var enqueue = connection.CreateCommand())
            {
                enqueue.CommandText = """
                    INSERT INTO slice_jobs (id, job_type, args, next_run_at)
                    VALUES (@jid, @type, @args, now())
                    """;
                enqueue.Parameters.AddWithValue("jid", Guid.CreateVersion7());
                enqueue.Parameters.AddWithValue("type", type);
                enqueue.Parameters.Add(PostgresJobsSchema.Jsonb("args", args));
                await enqueue.ExecuteNonQueryAsync(ct);
            }
            await using (var advance = connection.CreateCommand())
            {
                advance.CommandText = "UPDATE slice_recurring_jobs SET next_run_at = now() + (interval_ms || ' milliseconds')::interval WHERE id = @id";
                advance.Parameters.AddWithValue("id", id);
                await advance.ExecuteNonQueryAsync(ct);
            }
        }

        await tx.CommitAsync(ct);
    }
}

public static class PostgresBackgroundJobsRegistration
{
    /// <summary>Replaces the in-memory job managers with durable Postgres-backed ones (queue table +
    /// FOR UPDATE SKIP LOCKED worker + recurring scheduler). Pass a connection string to register the
    /// shared data source here.</summary>
    public static IServiceCollection AddSlicePostgresBackgroundJobs(
        this IServiceCollection services, string? connectionString = null)
    {
        if (connectionString is not null)
            services.AddSlicePostgres(connectionString);

        services.AddPostgresSchema(PostgresJobsSchema.Ddl);
        services.RemoveAll<IBackgroundJobManager>();
        services.RemoveAll<IRecurringJobManager>();
        services.AddSingleton<IBackgroundJobManager, PostgresBackgroundJobManager>();
        services.AddSingleton<IRecurringJobManager, PostgresRecurringJobManager>();
        services.AddHostedService<PostgresJobWorker>();
        services.AddHostedService<PostgresRecurringScheduler>();
        return services;
    }
}
