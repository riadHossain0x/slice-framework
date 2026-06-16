# Slice.BackgroundWorkers

> Lightweight periodic background workers — each ticks on its own interval inside a fresh DI scope.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

Provides the `IBackgroundWorker` abstraction for recurring, long-lived periodic work (e.g. polling, cleanup, heartbeat tasks) — distinct from the one-shot/enqueued jobs in [`Slice.BackgroundJobs`](../Slice.BackgroundJobs/). A single hosted `BackgroundWorkerManager` drives every registered worker concurrently, each on its own `Period`, executing `DoWorkAsync` in a freshly created DI scope so scoped services are valid each tick.

## Dependencies

- **Slice:** `Slice.Core`, `Slice.Modularity`, `Slice.Application`
- **Third-party:** `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Hosting.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`

## Module & registration

`SliceBackgroundWorkersModule` depends on `SliceApplicationModule` and registers the `BackgroundWorkerManager` hosted service. You register your own `IBackgroundWorker` implementations in DI.

```csharp
[DependsOn(typeof(SliceApplicationModule))]
public sealed class SliceBackgroundWorkersModule : SliceModule { /* adds the hosted manager */ }

// Register your workers:
services.AddSingleton<IBackgroundWorker, OutboxFlushWorker>();
```

## Key types

| Type | Kind | Description |
|---|---|---|
| `IBackgroundWorker` | interface | A recurring worker: `TimeSpan Period { get; }` and `Task DoWorkAsync(IServiceProvider scopedServices, CancellationToken ct)`. |
| `BackgroundWorkerManager` | `BackgroundService` | Runs every registered `IBackgroundWorker` concurrently on its own period, each in its own scope. |
| `SliceBackgroundWorkersModule` | `SliceModule` (`[DependsOn(SliceApplicationModule)]`) | Adds `BackgroundWorkerManager` as a hosted service. |

## Usage

Implement a periodic worker and register it:

```csharp
public sealed class OutboxFlushWorker : IBackgroundWorker
{
    public TimeSpan Period => TimeSpan.FromSeconds(10);

    public async Task DoWorkAsync(IServiceProvider scopedServices, CancellationToken ct)
    {
        // scopedServices is a fresh scope created per tick — resolve scoped deps here
        var outbox = scopedServices.GetRequiredService<IOutboxProcessor>();
        await outbox.FlushPendingAsync(ct);
    }
}

// Registration (in a module's ConfigureServices or host startup)
services.AddSingleton<IBackgroundWorker, OutboxFlushWorker>();
```

## Notes

- **Scope per tick:** `DoWorkAsync` receives a fresh `IServiceProvider` from `IServiceScopeFactory.CreateScope()`; do not hold references across ticks.
- **Period applies before work:** the manager `await Task.Delay(Period, ct)` *then* runs `DoWorkAsync` in a loop — so the first run happens after one `Period`, and `Period` is the delay between completion-to-next-start, not a fixed-rate schedule.
- **Resilience:** exceptions thrown by a worker are caught and logged (`BackgroundWorkerManager`'s logger); the worker keeps looping. A cancelled `Task.Delay` (shutdown) breaks the loop cleanly.
- **Concurrency:** all workers run via `Task.WhenAll`, so they execute in parallel and independently.
- Register `IBackgroundWorker` implementations yourself; the module only adds the manager.
