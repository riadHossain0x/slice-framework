# Slice.BackgroundJobs.Hangfire

> Hangfire-backed adapter for the Slice background-job abstractions — durable, distributed execution with no changes to job code.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

Maps Slice's `IBackgroundJobManager` and `IRecurringJobManager` onto Hangfire's client and recurring API. Your jobs continue to implement the same `IBackgroundJob<TArgs>` interface from [`Slice.BackgroundJobs`](../Slice.BackgroundJobs/) — Hangfire only changes *how* enqueued work is stored and executed. A single `HangfireJobDispatcher` is the only Hangfire-visible method: Hangfire serialises the closed-generic call plus args, and at execution time the dispatcher resolves the matching `IBackgroundJob<TArgs>` from DI.

## Dependencies

- **Slice:** `Slice.BackgroundJobs`, `Slice.Core`, `Slice.Modularity`
- **Third-party:** `Hangfire.AspNetCore`, `Hangfire.Core`, `Microsoft.Extensions.DependencyInjection.Abstractions`

## Module & registration

There is no `SliceModule` here — registration is done through the `AddSliceHangfire` extension, which **replaces** any in-memory managers registered by `SliceBackgroundJobsModule`. You supply Hangfire storage/server configuration; remember to add a Hangfire server in the host.

```csharp
services.AddSliceHangfire(cfg => cfg
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseInMemoryStorage()); // or a SQL/Redis storage

// Run the worker(s) that process enqueued jobs:
services.AddHangfireServer();
```

## Key types

| Type | Kind | Description |
|---|---|---|
| `HangfireRegistration` | static class | Provides `AddSliceHangfire(this IServiceCollection, Action<IGlobalConfiguration>)`; swaps in the Hangfire-backed managers. |
| `HangfireBackgroundJobManager` | class (`IBackgroundJobManager`) | Enqueues/schedules via Hangfire's `IBackgroundJobClient`. |
| `HangfireRecurringJobManager` | class (`IRecurringJobManager`) | Registers recurring jobs via Hangfire's `IRecurringJobManager`. |
| `HangfireJobDispatcher` | class | The single Hangfire-visible method `ExecuteAsync<TArgs>(TArgs args)` that resolves and runs the real job. |

## Usage

Job definitions are identical to the in-memory provider — only the registration changes:

```csharp
public sealed record GenerateReportArgs(Guid TenantId, DateOnly Date);

public sealed class GenerateReportJob(IReportService reports) : IBackgroundJob<GenerateReportArgs>
{
    public Task ExecuteAsync(GenerateReportArgs args, CancellationToken ct)
        => reports.GenerateAsync(args.TenantId, args.Date, ct);
}

// Startup
services.AddBackgroundJobHandlers(typeof(GenerateReportJob).Assembly); // from Slice.BackgroundJobs
services.AddSliceHangfire(cfg => cfg.UseInMemoryStorage());
services.AddHangfireServer();

// Enqueue exactly as before — now backed by Hangfire
await jobs.EnqueueAsync(new GenerateReportArgs(tenantId, today));
await jobs.EnqueueAsync(new GenerateReportArgs(tenantId, today), delay: TimeSpan.FromHours(1));
recurring.AddOrUpdate("daily-report", new GenerateReportArgs(tenantId, today), TimeSpan.FromHours(24));
```

## Notes

- **Replaces the default manager:** `AddSliceHangfire` calls `RemoveAll<IBackgroundJobManager>()` and `RemoveAll<IRecurringJobManager>()`, then registers the Hangfire-backed versions as **transient**. The in-memory `BackgroundJobWorker`/`RecurringJobScheduler` hosted services from `SliceBackgroundJobsModule` are not removed, so prefer registering only one provider, or omit the in-memory module when using Hangfire.
- **You must add a Hangfire server** (`services.AddHangfireServer()`) — `AddSliceHangfire` configures the client/storage only; without a server, enqueued jobs are stored but never run.
- **`EnqueueAsync`** returns the Hangfire job id. A non-zero `delay` uses `Schedule`; otherwise `Enqueue`.
- **Recurring intervals are coarsened to Hangfire cron granularity:** `<= 1 min` → `Cron.Minutely()`, `< 1 hour` → `Cron.Hourly()`, otherwise `Cron.Daily()`.
- **Cancellation:** the dispatcher invokes jobs with `CancellationToken.None`.
