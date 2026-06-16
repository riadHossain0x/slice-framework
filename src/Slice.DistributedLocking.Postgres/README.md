# Slice.DistributedLocking.Postgres

> A cross-node `IDistributedLock` using PostgreSQL session-level advisory locks — no TTL, auto-released when the session ends.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md), the [docs](../../docs/) and the [PostgreSQL stack guide](../../docs/postgresql-stack.md).

## Overview

This package replaces the registered `IDistributedLock` with `PostgresDistributedLock`, which acquires a cross-node lock via a PostgreSQL **session-level advisory lock**. The key is hashed to a `bigint` with `hashtextextended(key, 0)` and the lock is held on a dedicated connection for the lifetime of the returned handle; disposing the handle unlocks and returns the connection to the pool. Because the lock is tied to the session, a crashed holder's lock is released automatically when its session ends — no TTL is required.

## Dependencies

- **Slice:** `Slice.DistributedLocking`, `Slice.Postgres`
- **Third-party:** `Npgsql`, `Microsoft.Extensions.DependencyInjection.Abstractions`

## Registration

```csharp
// With a connection string: registers the shared NpgsqlDataSource here too.
services.AddSlicePostgresDistributedLock(connectionString);

// Or omit it to reuse a data source already registered by AddSlicePostgres / AddSlicePostgresStack:
services.AddSlicePostgresDistributedLock();
```

`AddSlicePostgresDistributedLock` does the swap: `RemoveAll<IDistributedLock>()` followed by `AddSingleton<IDistributedLock, PostgresDistributedLock>()`. When a connection string is passed it calls `AddSlicePostgres(connectionString)` first; otherwise it reuses the shared data source.

## Key types

| Type | Kind | Description |
|---|---|---|
| `PostgresDistributedLock` | sealed class (`IDistributedLock`) | `TryAcquireAsync(key, timeout?, ct)` — polls `pg_try_advisory_lock(hashtextextended(key, 0))` on a held connection until acquired or the timeout deadline, returning an `IAsyncDisposable` handle (or `null`). |
| `PostgresDistributedLockRegistration` | static class | `AddSlicePostgresDistributedLock(connectionString?)` DI extension. |

## Schema / storage

None. Advisory locks are an in-server primitive — no table is created and no `AddPostgresSchema` contributor is registered.

## Usage

```csharp
builder.Services.AddSlicePostgres(connectionString);
builder.Services.AddSlicePostgresDistributedLock();

// Acquire; null means it couldn't be taken within the timeout.
await using var handle = await distributedLock.TryAcquireAsync(
    "orders:rebuild", timeout: TimeSpan.FromSeconds(5));
if (handle is null) return;   // someone else holds it

// ... critical section ...
// disposing 'handle' unlocks and returns the dedicated connection to the pool
```

## Notes

- **Session semantics, no TTL:** the lock lives for the session holding it; disposing the handle calls `pg_advisory_unlock` and then disposes the connection (which also releases any held locks). A crashed process releases its lock when Postgres closes the session.
- **Dedicated connection per handle:** each acquired lock holds one connection from the pool for its lifetime — size the pool accordingly for long-held locks.
- **Polling:** when contended, acquisition retries every 50 ms until the deadline; a `timeout` of `null`/zero means a single non-blocking attempt.
- On exception (or timeout) the connection is disposed back to the pool.
