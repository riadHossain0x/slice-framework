# Slice.DistributedLocking

> In-process implementation of the `IDistributedLock` seam — per-key mutual exclusion for single-node deployments.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

The `IDistributedLock` abstraction itself lives in `Slice.Core` (`Slice.Core.Ambient`), where the default registration is `NullDistributedLock` — a no-op that always "acquires". This project supplies `LocalDistributedLock`, a real in-process implementation backed by a per-key `SemaphoreSlim`, giving correct mutual exclusion *within a single process*. For genuine cross-node coordination, replace it with the [Redis provider](../Slice.DistributedLocking.Redis/).

## Dependencies

- **Slice:** `Slice.Core`, `Slice.Modularity`, `Slice.Application`
- **Third-party:** `Microsoft.Extensions.DependencyInjection.Abstractions`

## Module & registration

`SliceDistributedLockingModule` depends on `SliceApplicationModule` and registers `LocalDistributedLock` by convention (it implements `ISingletonDependency`), overriding the Core `NullDistributedLock` default.

```csharp
[DependsOn(typeof(SliceApplicationModule))]
public sealed class SliceDistributedLockingModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
        => context.Services.AddSliceConventions(typeof(SliceDistributedLockingModule).Assembly);
}
```

## Key types

| Type | Kind | Description |
|---|---|---|
| `IDistributedLock` | interface *(in `Slice.Core.Ambient`)* | `Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan? timeout = null, CancellationToken ct = default)` — returns a handle on success, `null` if held elsewhere. |
| `NullDistributedLock` | class *(in `Slice.Core.Ambient`)* | Core default; always acquires (single-node / no coordination). |
| `LocalDistributedLock` | class (`IDistributedLock`, `ISingletonDependency`) | In-process lock using a per-key `SemaphoreSlim`. |
| `SliceDistributedLockingModule` | `SliceModule` (`[DependsOn(SliceApplicationModule)]`) | Registers `LocalDistributedLock`, replacing the Core null lock. |

## Usage

Acquire a lock with `TryAcquireAsync`; the returned handle is an `IAsyncDisposable` — disposing it releases the lock. A `null` result means the lock was not acquired.

```csharp
public sealed class ImportRunner(IDistributedLock locks)
{
    public async Task RunAsync(CancellationToken ct)
    {
        await using var handle = await locks.TryAcquireAsync(
            "import:nightly", timeout: TimeSpan.FromSeconds(5), ct);

        if (handle is null)
            return; // another holder owns the lock — skip

        // critical section — released on dispose
        await DoImportAsync(ct);
    }
}
```

## Notes

- **Handle type:** the lock handle is `IAsyncDisposable` (there is no separate named handle interface). Always `await using` it — disposal calls `SemaphoreSlim.Release()`.
- **`timeout` semantics:** defaults to `TimeSpan.Zero` (try-once, non-blocking). Pass a positive timeout to wait. The token cancels the wait.
- **Lifetime:** `LocalDistributedLock` is a singleton; its per-key semaphores are stored in a `ConcurrentDictionary` and reused across calls.
- **Single-node only:** correctness holds within one process. Across multiple instances/nodes it provides no coordination — use `Slice.DistributedLocking.Redis` for that.
