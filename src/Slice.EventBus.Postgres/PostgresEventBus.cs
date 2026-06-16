using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Slice.Domain.Events;
using Slice.Postgres;

namespace Slice.EventBus.Postgres;

internal static class PostgresEventBusSchema
{
    public const string Channel = "slice_events";

    public const string Ddl = """
        CREATE TABLE IF NOT EXISTS slice_event_queue (
            id           uuid PRIMARY KEY,
            event_name   text NOT NULL,
            payload      bytea NOT NULL,
            message_id   text NOT NULL,
            created_at   timestamptz NOT NULL DEFAULT now(),
            processed_at timestamptz NULL,
            retry_count  int NOT NULL DEFAULT 0
        );
        CREATE INDEX IF NOT EXISTS ix_slice_event_queue_unprocessed
            ON slice_event_queue (created_at) WHERE processed_at IS NULL;
        """;
}

/// <summary>
/// Publishes distributed events to a durable Postgres queue (<c>slice_event_queue</c>) and fires
/// <c>NOTIFY slice_events</c> so the consumer drains immediately instead of waiting for its poll.
/// </summary>
public sealed class PostgresEventPublisher(NpgsqlDataSource dataSource, IDistributedEventTypeRegistry registry)
    : IDistributedEventPublisher
{
    public async Task PublishAsync(IDistributedEvent @event, string messageId, CancellationToken ct = default)
    {
        var name = registry.GetName(@event.GetType());
        var payload = JsonSerializer.SerializeToUtf8Bytes(@event, @event.GetType());

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO slice_event_queue (id, event_name, payload, message_id)
            VALUES (@id, @name, @payload, @mid);
            SELECT pg_notify(@chan, @id::text);
            """;
        cmd.Parameters.AddWithValue("id", Guid.CreateVersion7());
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("payload", payload);
        cmd.Parameters.AddWithValue("mid", messageId);
        cmd.Parameters.AddWithValue("chan", PostgresEventBusSchema.Channel);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

/// <summary>
/// Drains <c>slice_event_queue</c> and dispatches each row to local handlers via
/// <see cref="IDistributedEventConsumer"/>. Wakes on <c>LISTEN slice_events</c> for low latency and
/// polls every few seconds as a durable fallback. Rows are claimed with <c>FOR UPDATE SKIP LOCKED</c>
/// so multiple instances share the work without double-processing.
/// </summary>
public sealed class PostgresEventConsumer(
    NpgsqlDataSource dataSource, IServiceScopeFactory scopeFactory, ILogger<PostgresEventConsumer> logger)
    : BackgroundService
{
    private const int BatchSize = 50;
    private const int MaxRetries = 5;
    private static readonly TimeSpan FallbackPoll = TimeSpan.FromSeconds(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var listen = await dataSource.OpenConnectionAsync(stoppingToken);
        await using (var cmd = listen.CreateCommand())
        {
            cmd.CommandText = $"LISTEN {PostgresEventBusSchema.Channel}";
            await cmd.ExecuteNonQueryAsync(stoppingToken);
        }

        await DrainAsync(stoppingToken);   // pick up anything already queued

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await listen.WaitAsync(FallbackPoll, stoppingToken); }   // returns on NOTIFY or timeout
            catch (OperationCanceledException) { break; }

            await DrainAsync(stoppingToken);
        }
    }

    private async Task DrainAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await using var connection = await dataSource.OpenConnectionAsync(ct);
                await using var tx = await connection.BeginTransactionAsync(ct);

                var batch = await ClaimBatchAsync(connection, ct);
                if (batch.Count == 0)
                {
                    await tx.RollbackAsync(ct);
                    return;
                }

                foreach (var row in batch)
                {
                    try
                    {
                        using var scope = scopeFactory.CreateScope();
                        var dispatcher = scope.ServiceProvider.GetRequiredService<IDistributedEventConsumer>();
                        await dispatcher.ConsumeAsync(row.EventName, row.MessageId, row.Payload, ct);
                        await MarkAsync(connection, row.Id, processed: true, error: null, ct);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Postgres event {Id} dispatch failed", row.Id);
                        await MarkAsync(connection, row.Id, processed: false, error: ex.Message, ct);
                    }
                }

                await tx.CommitAsync(ct);
                if (batch.Count < BatchSize) return;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Postgres event drain failed");
        }
    }

    private static async Task<List<QueueRow>> ClaimBatchAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT id, event_name, message_id, payload
            FROM slice_event_queue
            WHERE processed_at IS NULL AND retry_count < {MaxRetries}
            ORDER BY created_at
            FOR UPDATE SKIP LOCKED
            LIMIT {BatchSize}
            """;
        var rows = new List<QueueRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(new QueueRow(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), (byte[])reader[3]));
        return rows;
    }

    private static async Task MarkAsync(NpgsqlConnection connection, Guid id, bool processed, string? error, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = processed
            ? "UPDATE slice_event_queue SET processed_at = now() WHERE id = @id"
            : "UPDATE slice_event_queue SET retry_count = retry_count + 1 WHERE id = @id";
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private readonly record struct QueueRow(Guid Id, string EventName, string MessageId, byte[] Payload);
}

public static class PostgresEventBusRegistration
{
    /// <summary>Routes distributed events through a Postgres queue (publisher + LISTEN/NOTIFY consumer)
    /// instead of local loopback. Pass a connection string to register the shared data source here.</summary>
    public static IServiceCollection AddSlicePostgresEventBus(
        this IServiceCollection services, string? connectionString = null)
    {
        if (connectionString is not null)
            services.AddSlicePostgres(connectionString);

        services.AddPostgresSchema(PostgresEventBusSchema.Ddl);
        services.RemoveAll<IDistributedEventPublisher>();
        services.AddSingleton<IDistributedEventPublisher, PostgresEventPublisher>();
        services.AddHostedService<PostgresEventConsumer>();
        return services;
    }
}
