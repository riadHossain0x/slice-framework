# Slice.EventBus.Postgres

> A durable, Postgres-backed distributed event bus that swaps Slice's local-loopback publisher for a queue table driven by `LISTEN`/`NOTIFY`.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md), the [docs](../../docs/) and the [PostgreSQL stack guide](../../docs/postgresql-stack.md).

## Overview

This adapter replaces the default `IDistributedEventPublisher` (which dispatches to in-process handlers immediately) with one that persists each event into the `slice_event_queue` table and fires `pg_notify('slice_events', id)`. A hosted `BackgroundService` consumer listens on that channel — and polls as a fallback — draining the queue and dispatching each row to local handlers through `IDistributedEventConsumer`. The result is durable, at-least-once cross-process delivery over a single Postgres instance, with no extra broker. It is the natural downstream of the EF outbox: outbox → this publisher → queue + `NOTIFY` → consumer → handlers.

## Dependencies

- **Slice:** `Slice.EventBus`, `Slice.Core`, `Slice.Postgres`
- **Third-party:** `Npgsql`, `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Hosting.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`

## Registration

```csharp
// Standalone: register the shared Npgsql data source here.
builder.Services.AddSlicePostgresEventBus("Host=localhost;Database=app;Username=app;Password=secret");

// Or reuse an already-registered shared NpgsqlDataSource (e.g. under the PostgresStack).
builder.Services.AddSlicePostgresEventBus();
```

`AddSlicePostgresEventBus(connectionString?)`:

- Calls `AddSlicePostgres(connectionString)` only when a connection string is supplied (otherwise it reuses the existing shared data source).
- Registers the schema DDL via `AddPostgresSchema(...)`.
- `RemoveAll<IDistributedEventPublisher>()` then registers `PostgresEventPublisher` as a **singleton**.
- Registers `PostgresEventConsumer` as a hosted service.

## Key types

| Type | Kind | Description |
|---|---|---|
| `PostgresEventPublisher` | `sealed class : IDistributedEventPublisher` | `PublishAsync(IDistributedEvent @event, string messageId, CancellationToken ct = default)` — resolves the wire name via `IDistributedEventTypeRegistry`, serializes the event to UTF-8 JSON bytes, `INSERT`s into `slice_event_queue` and calls `pg_notify`. |
| `PostgresEventConsumer` | `sealed class : BackgroundService` | `LISTEN slice_events`, drains backlog on start, then waits on `NOTIFY`/timeout and re-drains. Claims rows with `FOR UPDATE SKIP LOCKED`, dispatches via a scoped `IDistributedEventConsumer`, marks processed or bumps `retry_count`. |
| `PostgresEventBusRegistration` | `static class` | Hosts the `AddSlicePostgresEventBus` extension. |
| `PostgresEventBusSchema` | `internal static class` | Holds the channel constant `Channel = "slice_events"` and the table DDL. |

Tuning constants on `PostgresEventConsumer`: `BatchSize = 50`, `MaxRetries = 5`, `FallbackPoll = 3s`.

## Schema / storage

`AddPostgresSchema` applies this DDL (idempotent):

```sql
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
```

- `id` is a v7 GUID generated per publish; it is also the `NOTIFY` payload (`pg_notify('slice_events', id::text)`).
- `payload` is the event serialized with `JsonSerializer.SerializeToUtf8Bytes` against its concrete type.
- `message_id` is the caller-supplied idempotency key, carried through to the consumer's inbox check.
- The partial index covers the unprocessed-rows scan ordered by `created_at`.

## Usage

You normally don't call the publisher directly — events flow through Slice's event bus / outbox. To consume, register your handlers and an inbox in the `Slice.EventBus` layer; the consumer dispatches each queued row through `IDistributedEventConsumer.ConsumeAsync(eventName, messageId, payload, ct)`, which dedups via `IInboxStore`, resolves the type via the registry, deserializes and republishes to local handlers.

```csharp
// Wire-up (host):
builder.Services.AddSlicePostgresEventBus(connectionString);

// Publishing (via the framework's IDistributedEventPublisher / outbox):
public sealed class OrderPlaced : IDistributedEvent
{
    public required Guid OrderId { get; init; }
}

await publisher.PublishAsync(new OrderPlaced { OrderId = id }, messageId: Guid.NewGuid().ToString(), ct);
// → INSERT slice_event_queue + NOTIFY slice_events → PostgresEventConsumer dispatches to handlers
```

## Notes

- **Lifetimes:** publisher is a singleton; consumer is a hosted `BackgroundService`. Each operation opens its own connection from the shared `NpgsqlDataSource`.
- **Delivery is at-least-once.** A handler may run more than once (e.g. retried row, multi-instance race). Idempotency is enforced downstream by `DistributedEventConsumer` calling `IInboxStore.TryMarkProcessedAsync(messageId)` — the same `messageId` is skipped on a second delivery (default `NullInboxStore` does not dedup; supply a durable inbox for true exactly-once-effect).
- **`FOR UPDATE SKIP LOCKED`** lets multiple app instances drain the same queue without double-claiming a row; unclaimed rows are skipped rather than blocked.
- **Retries:** failed dispatch increments `retry_count`; rows with `retry_count >= MaxRetries (5)` are no longer claimed (effectively dead-lettered in place).
- **Latency vs. durability:** `NOTIFY` wakes the consumer instantly; the 3-second fallback poll guarantees progress even if a `NOTIFY` is missed (e.g. connection blip), and picks up the backlog on startup.
- Batches drain in a loop until fewer than `BatchSize` rows remain, so bursts clear quickly.
