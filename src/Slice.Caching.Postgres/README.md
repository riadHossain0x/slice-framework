# Slice.Caching.Postgres

> An `IDistributedCache` backed by a single Postgres table (`slice_cache`), with absolute/sliding expiry and a background sweeper.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md), the [docs](../../docs/) and the [PostgreSQL stack guide](../../docs/postgresql-stack.md).

## Overview

This package replaces the registered `IDistributedCache` with `PostgresDistributedCache`, which stores entries as `bytea` rows in the `slice_cache` table. Absolute and sliding expirations are honoured (sliding entries have their window extended on read, capped by the absolute expiry), and `PostgresCacheSweeper` periodically deletes expired rows so the table doesn't grow unbounded. Slice's typed, tenant-aware `ISliceCache` rides on top of the standard `IDistributedCache` abstraction unchanged.

## Dependencies

- **Slice:** `Slice.Caching`, `Slice.Postgres`
- **Third-party:** `Npgsql`, `Microsoft.Extensions.Caching.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Hosting.Abstractions`

## Registration

```csharp
// With a connection string: registers the shared NpgsqlDataSource here too.
services.AddSlicePostgresCache(connectionString);

// Or omit it to reuse a data source already registered by AddSlicePostgres / AddSlicePostgresStack:
services.AddSlicePostgresCache();
```

`AddSlicePostgresCache` contributes the `slice_cache` DDL via `AddPostgresSchema`, then does the swap: `RemoveAll<IDistributedCache>()` followed by `AddSingleton<IDistributedCache, PostgresDistributedCache>()`, and registers `PostgresCacheSweeper` as a hosted service. When a connection string is passed it calls `AddSlicePostgres(connectionString)` first; otherwise it reuses the shared data source.

## Key types

| Type | Kind | Description |
|---|---|---|
| `PostgresDistributedCache` | sealed class (`IDistributedCache`) | Cache over `slice_cache`; implements sync + async `Get/Set/Refresh/Remove`, honours absolute/sliding expiry, lazily deletes expired rows on read. |
| `PostgresCacheSweeper` | sealed class (`BackgroundService`) | Every 1 minute deletes rows where `expires_at <= now()`; best-effort (swallows errors, retries next tick). |
| `PostgresCacheRegistration` | static class | `AddSlicePostgresCache(connectionString?)` DI extension. |

## Schema / storage

DDL (`PostgresDistributedCache.Ddl`) is contributed to `AddPostgresSchema`, so `PostgresSchemaInitializer` creates it at startup:

```sql
CREATE TABLE IF NOT EXISTS slice_cache (
    key                 text PRIMARY KEY,
    value               bytea NOT NULL,
    expires_at          timestamptz NULL,
    sliding_seconds     double precision NULL,
    absolute_expires_at timestamptz NULL
);
CREATE INDEX IF NOT EXISTS ix_slice_cache_expires ON slice_cache (expires_at);
```

Writes use `INSERT … ON CONFLICT (key) DO UPDATE`. `expires_at` is the effective next-expiry (sliding window start, capped by `absolute_expires_at`); `sliding_seconds` records the sliding window so reads can re-extend it.

## Usage

```csharp
builder.Services.AddSlicePostgres(connectionString);
builder.Services.AddSlicePostgresCache();   // reuses the shared data source

// Anywhere an IDistributedCache (or Slice's ISliceCache on top of it) is injected:
await cache.SetAsync("k", bytes, new DistributedCacheEntryOptions
{
    SlidingExpiration = TimeSpan.FromMinutes(10),
    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1),
});
var hit = await cache.GetAsync("k");   // slides the window on access, capped by the absolute expiry
```

## Notes

- **Sliding-on-read:** `GetAsync` extends `expires_at` to `now + sliding_seconds` (capped by `absolute_expires_at`); `RefreshAsync` is just the same read path, so a no-value refresh also slides.
- **Two-layer expiry:** entries past `expires_at` are removed lazily on read *and* swept every minute by `PostgresCacheSweeper`.
- **Shared pool:** when called without a connection string it reuses the stack's single `NpgsqlDataSource`.
- All timestamps are UTC.
