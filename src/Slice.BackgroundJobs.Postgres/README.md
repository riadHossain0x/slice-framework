# Slice.BackgroundJobs.Postgres

> Durable, Postgres-backed background and recurring jobs — swaps Slice's in-memory job managers for queue tables drained by `FOR UPDATE SKIP LOCKED` workers.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md), the [docs](../../docs/) and the [PostgreSQL stack guide](../../docs/postgresql-stack.md).

## Overview

This adapter replaces the framework's default `IBackgroundJobManager` and `IRecurringJobManager` with Postgres-backed implementations. Fire-and-forget work is persisted as rows in `slice_jobs`; recurring definitions live in `slice_recurring_jobs`. Two hosted services poll those tables: `PostgresJobWorker` runs due jobs, and `PostgresRecurringScheduler` materializes due recurring definitions into `slice_jobs`. The args type is stored as its `AssemblyQualifiedName` and the args themselves as `jsonb`, so the worker can rehydrate the type via reflection and resolve the matching `IBackgroundJob<TArgs>` from DI.

## Dependencies

- **Slice:** `Slice.BackgroundJobs`, `Slice.Core`, `Slice.Postgres`
- **Third-party:** `Npgsql` (incl. `NpgsqlTypes`), `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Hosting.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`

## Registration

```csharp
// Standalone: register the shared Npgsql data source here.
builder.Services.AddSlicePostgresBackgroundJobs("Host=localhost;Database=app;Username=app;Password=secret");

// Or reuse an already-registered shared NpgsqlDataSource (e.g. under the PostgresStack).
builder.Services.AddSlicePostgresBackgroundJobs();
```

`AddSlicePostgresBackgroundJobs(connectionString?)`:

- Calls `AddSlicePostgres(connectionString)` only when a connection string is supplied.
- Registers the schema DDL via `AddPostgresSchema(...)`.
- `RemoveAll<IBackgroundJobManager>()` and `RemoveAll<IRecurringJobManager>()`, then registers `PostgresBackgroundJobManager` and `PostgresRecurringJobManager` as **singletons**.
- Registers `PostgresJobWorker` and `PostgresRecurringScheduler` as hosted services.

## Key types

| Type | Kind | Description |
|---|---|---|
| `PostgresBackgroundJobManager` | `sealed class : IBackgroundJobManager` | `Task<string> EnqueueAsync<TArgs>(TArgs args, TimeSpan? delay = null)` — `INSERT`s into `slice_jobs` with `next_run_at = now() + delay`; returns the v7-GUID id as a string. |
| `PostgresRecurringJobManager` | `sealed class : IRecurringJobManager` | `void AddOrUpdate<TArgs>(string jobId, TArgs args, TimeSpan interval)` — upserts into `slice_recurring_jobs` (`ON CONFLICT (id) DO UPDATE`). |
| `PostgresJobWorker` | `sealed class : BackgroundService` | Polls `slice_jobs` for due, incomplete, under-retry-limit rows with `FOR UPDATE SKIP LOCKED`; rehydrates args type, resolves `IBackgroundJob<TArgs>`, runs it, marks complete or applies exponential backoff. |
| `PostgresRecurringScheduler` | `sealed class : BackgroundService` | Polls `slice_recurring_jobs` for due rows, enqueues a `slice_jobs` row for each, and advances `next_run_at` by `interval_ms`. |
| `PostgresBackgroundJobsRegistration` | `static class` | Hosts the `AddSlicePostgresBackgroundJobs` extension. |
| `PostgresJobsSchema` | `internal static class` | DDL, `MaxRetries = 5`, and the `Jsonb(name, json)` `NpgsqlParameter` helper. |

Tuning constants: worker `BatchSize = 25`, `Poll = 1s`; scheduler `Poll = 1s`; `MaxRetries = 5`.

## Schema / storage

`AddPostgresSchema` applies this DDL (idempotent):

```sql
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
```

- `job_type` is `typeof(TArgs).AssemblyQualifiedName` — the **args** type, not the handler type. It is rehydrated with `Type.GetType(typeName)`, then `typeof(IBackgroundJob<>).MakeGenericType(argsType)` is resolved from a DI scope and its `ExecuteAsync(args, ct)` invoked via reflection.
- `args` is `jsonb` (serialized with `JsonSerializer.Serialize`, read back as `args::text` and deserialized against the rehydrated type).
- `slice_recurring_jobs.id` is the caller-supplied stable `jobId` (text); upsert keeps a single definition per id.

## Usage

```csharp
// 1. Define args + a handler.
public sealed record SendWelcomeEmail(Guid UserId);

public sealed class SendWelcomeEmailJob(IEmailSender email) : IBackgroundJob<SendWelcomeEmail>
{
    public async Task ExecuteAsync(SendWelcomeEmail args, CancellationToken ct)
        => await email.SendWelcomeAsync(args.UserId, ct);
}

// 2. Register handler (your DI) + the Postgres adapter (host).
builder.Services.AddScoped<IBackgroundJob<SendWelcomeEmail>, SendWelcomeEmailJob>();
builder.Services.AddSlicePostgresBackgroundJobs(connectionString);

// 3. Enqueue fire-and-forget (optionally delayed).
string jobId = await jobs.EnqueueAsync(new SendWelcomeEmail(userId), delay: TimeSpan.FromMinutes(5));

// 4. Register recurring work.
recurring.AddOrUpdate("nightly-cleanup", new CleanupArgs(), interval: TimeSpan.FromHours(24));
```

## Notes

- **Lifetimes:** both managers are singletons; the worker and scheduler are hosted `BackgroundService`s. Each tick opens its own connection + transaction from the shared `NpgsqlDataSource`.
- **`FOR UPDATE SKIP LOCKED`** lets multiple instances poll the same tables without double-running a job; locked rows are skipped, not awaited.
- **Exponential backoff:** on failure the worker increments `retry_count`, records `error`, and pushes `next_run_at = now() + 2^retry_count seconds`. Rows at `retry_count >= MaxRetries (5)` are no longer picked up.
- **At-least-once execution:** a job's `ExecuteAsync` may run more than once (retry, multi-instance race, crash before commit). Keep handlers idempotent.
- **Type resolution gotcha:** because `job_type` is an `AssemblyQualifiedName`, the worker process must be able to load that assembly/type; renaming or moving the args type breaks rehydration of already-queued rows (`Unknown job args type` → counts as a failure/retry).
- The recurring scheduler advances `next_run_at` to `now() + interval_ms` (not strictly aligned to wall-clock cadence), so long ticks won't pile up missed runs.
