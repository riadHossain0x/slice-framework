# Slice.PostgresStack

> Convenience meta-package that wires the entire Postgres-backed Slice stack onto a single shared `NpgsqlDataSource` with one call.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md), the [docs](../../docs/) and the [PostgreSQL stack guide](../../docs/postgresql-stack.md).

## Overview

Rather than calling each Postgres adapter's `AddSlicePostgres…` extension by hand, this package offers `AddSlicePostgresStack`. It builds **one** shared `NpgsqlDataSource` up front (enabling pgvector mapping via `UseVector()` when the vector store is on) and then wires each adapter on top of that same data source. A `PostgresStackOptions` toggle controls which adapters are registered — caching, distributed locking, the event bus, background jobs, blob storing, and the vector store — all enabled by default. Every adapter swaps its corresponding framework default to its Postgres implementation.

## Dependencies

- **Slice:** `Slice.Postgres`, `Slice.EntityFrameworkCore.PostgreSQL`, `Slice.Caching.Postgres`, `Slice.DistributedLocking.Postgres`, `Slice.EventBus.Postgres`, `Slice.BackgroundJobs.Postgres`, `Slice.BlobStoring.Postgres`, `Slice.Vector.Postgres`
- **Third-party:** `Pgvector`, `Microsoft.Extensions.DependencyInjection.Abstractions`

## Registration

```csharp
// All adapters on (defaults).
builder.Services.AddSlicePostgresStack(connectionString);

// Selectively disable adapters.
builder.Services.AddSlicePostgresStack(connectionString, o =>
{
    o.VectorStore = false;   // skip pgvector (no UseVector() on the data source)
    o.BlobStoring = false;
});
```

`AddSlicePostgresStack(connectionString, Action<PostgresStackOptions>? configure = null)`:

1. Builds the options, applying `configure`.
2. Calls `AddSlicePostgres(connectionString, builder => { if (VectorStore) builder.UseVector(); })` to register the single shared `NpgsqlDataSource`.
3. For each enabled toggle, calls that adapter's parameterless extension — so it **reuses** the shared data source instead of registering its own:
   `AddSlicePostgresCache()`, `AddSlicePostgresDistributedLock()`, `AddSlicePostgresEventBus()`, `AddSlicePostgresBackgroundJobs()`, `AddSlicePostgresBlobStoring()`, `AddSlicePostgresVectorStore()`.

## Key types

| Type | Kind | Description |
|---|---|---|
| `PostgresStackOptions` | `sealed class` | Per-adapter toggles, all `true` by default: `Cache`, `DistributedLock`, `EventBus`, `BackgroundJobs`, `BlobStoring`, `VectorStore`. |
| `SlicePostgresStackRegistration` | `static class` | Hosts the `AddSlicePostgresStack` extension. |

## Schema / storage

This package creates no tables of its own. Each enabled adapter contributes its own DDL through `AddPostgresSchema` (e.g. `slice_event_queue` from the event bus; `slice_jobs` / `slice_recurring_jobs` from background jobs; cache, lock, blob, and vector tables from their respective adapters). All of it runs on the one shared connection pool. When `VectorStore` is enabled, the data source is built with `UseVector()` so pgvector types map correctly.

## Usage

Canonical host wiring — one shared `NpgsqlDataSource` powers the adapters *and* EF Core (data, outbox/inbox, auth, management):

```csharp
var connectionString = builder.Configuration.GetConnectionString("Postgres")!;

builder.Services.AddSlicePostgresStack(connectionString);

builder.Services.AddSliceDbContext<AppDbContext>((sp, o) =>
    o.UseSlicePostgres(sp.GetRequiredService<NpgsqlDataSource>()));
```

`AddSlicePostgresStack` registers the `NpgsqlDataSource`; `AddSliceDbContext<T>` pulls that same instance from DI via `UseSlicePostgres(...)`, so EF and every Postgres adapter share a single pool.

## Notes

- **Single pool:** the whole point is one `NpgsqlDataSource` — pass the connection string only to `AddSlicePostgresStack`, and let the individual adapters' parameterless overloads reuse it. Passing connection strings to adapters individually would re-register/duplicate the data source.
- **Order matters:** the data source is built first (with `UseVector()` conditionally applied) before any adapter is wired, so pgvector mapping is in place for the vector store.
- **Swap pattern:** each adapter `RemoveAll<...>()`s the framework default and registers its Postgres replacement (publisher/consumer, job managers/workers, cache, lock, etc.), giving you durable, cross-process behavior over plain Postgres without an external broker.
- **Toggles are additive opt-out:** everything is on by default; set a flag to `false` to skip both its registration and (for `VectorStore`) its `UseVector()` mapping.
