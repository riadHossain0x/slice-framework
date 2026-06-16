using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Slice.Postgres;

/// <summary>
/// A contributor of idempotent DDL for a Postgres-backed adapter. Each adapter registers one; the
/// <see cref="PostgresSchemaInitializer"/> runs them all once at startup. The SQL must be safe to run
/// repeatedly (e.g. <c>CREATE TABLE IF NOT EXISTS …</c>, <c>CREATE EXTENSION IF NOT EXISTS …</c>) and
/// may contain multiple statements.
/// </summary>
public interface IPostgresSchema
{
    string Ddl { get; }
}

/// <summary>
/// Runs every registered <see cref="IPostgresSchema"/> once on startup, guarded by a session advisory
/// lock so concurrent app instances don't race to create the same objects.
/// </summary>
public sealed class PostgresSchemaInitializer(
    NpgsqlDataSource dataSource,
    IEnumerable<IPostgresSchema> schemas,
    ILogger<PostgresSchemaInitializer> logger) : IHostedService
{
    // Arbitrary, stable key shared by all Slice schema initializers.
    private const long AdvisoryKey = 4_242_424_242L;

    public async Task StartAsync(CancellationToken ct)
    {
        var ddls = schemas.Select(s => s.Ddl).Where(d => !string.IsNullOrWhiteSpace(d)).ToList();
        if (ddls.Count == 0)
            return;

        await using var connection = await dataSource.OpenConnectionAsync(ct);

        await using (var lockCmd = connection.CreateCommand())
        {
            lockCmd.CommandText = "SELECT pg_advisory_lock(@k)";
            lockCmd.Parameters.AddWithValue("k", AdvisoryKey);
            await lockCmd.ExecuteNonQueryAsync(ct);
        }

        try
        {
            foreach (var ddl in ddls)
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = ddl;
                await cmd.ExecuteNonQueryAsync(ct);
            }
            logger.LogInformation("Slice Postgres schema ensured ({Count} contributors).", ddls.Count);
        }
        finally
        {
            await using var unlock = connection.CreateCommand();
            unlock.CommandText = "SELECT pg_advisory_unlock(@k)";
            unlock.Parameters.AddWithValue("k", AdvisoryKey);
            await unlock.ExecuteNonQueryAsync(ct);
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
