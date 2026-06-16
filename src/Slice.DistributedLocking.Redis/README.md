# Slice.DistributedLocking.Redis

> Redis-backed `IDistributedLock` for true cross-node mutual exclusion in multi-instance deployments.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

Implements the `IDistributedLock` seam (defined in `Slice.Core.Ambient`) against Redis via StackExchange.Redis. Acquisition uses `SET key token NX PX ttl` (set-if-not-exists with a TTL), and release runs a compare-and-delete Lua script so a holder only ever deletes its *own* lock. This replaces the in-process [local lock](../Slice.DistributedLocking/) when you run more than one node and need coordination across processes.

## Dependencies

- **Slice:** `Slice.DistributedLocking` (transitively `Slice.Core` via the `IDistributedLock` seam)
- **Third-party:** `StackExchange.Redis`, `Microsoft.Extensions.DependencyInjection.Abstractions`

## Module & registration

There is no `SliceModule` here — wiring is done with the `AddSliceRedisDistributedLock` extension, which **replaces** any existing `IDistributedLock` registration and registers a shared `IConnectionMultiplexer`.

```csharp
services.AddSliceRedisDistributedLock("localhost:6379");
```

## Key types

| Type | Kind | Description |
|---|---|---|
| `RedisDistributedLock` | class (`IDistributedLock`) | Redis lock: `SET … NX PX` to acquire, Lua compare-and-delete to release. |
| `RedisDistributedLockRegistration` | static class | Provides `AddSliceRedisDistributedLock(this IServiceCollection, string connectionString)`. |

## Usage

Consume `IDistributedLock` exactly as with the local provider — only the registration changes:

```csharp
// Startup
services.AddSliceRedisDistributedLock(configuration.GetConnectionString("Redis")!);

// Anywhere
public sealed class ScheduledJob(IDistributedLock locks)
{
    public async Task RunAsync(CancellationToken ct)
    {
        await using var handle = await locks.TryAcquireAsync(
            "job:scheduled", timeout: TimeSpan.FromSeconds(10), ct);

        if (handle is null)
            return; // another node holds the lock

        await DoWorkAsync(ct); // released (compare-and-delete) on dispose
    }
}
```

## Notes

- **Replaces the local lock:** `AddSliceRedisDistributedLock` calls `RemoveAll<IDistributedLock>()`, registers `IConnectionMultiplexer` via `TryAddSingleton` (a single shared `ConnectionMultiplexer.Connect(connectionString)`), then adds `RedisDistributedLock` as a singleton.
- **TTL is fixed at 30 seconds** — the lock auto-expires after that even if the holder crashes without disposing. Keep critical sections shorter than the TTL.
- **`timeout` semantics:** defaults to `TimeSpan.Zero` (single attempt). With a positive timeout, acquisition retries with a 50 ms poll interval until the deadline; the token cancels the wait.
- **Safe release:** disposal runs a Lua script that deletes the key only if it still holds this acquisition's unique token, so an expired-then-reacquired lock is never released by a stale holder.
- **Handle type:** the returned handle is `IAsyncDisposable`; `null` means the lock was not acquired.
