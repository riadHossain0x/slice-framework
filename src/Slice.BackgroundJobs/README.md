# Slice.BackgroundJobs

> Fire-and-forget and recurring background-job abstractions with a default in-process (channel-based) implementation.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

Defines the background-job seam used across Slice: jobs are typed units of work (`IBackgroundJob<TArgs>`) enqueued through `IBackgroundJobManager` or scheduled through `IRecurringJobManager`. The default implementation is fully in-process — an unbounded `System.Threading.Channels` queue drained by a hosted `BackgroundJobWorker`, plus a polling `RecurringJobScheduler`. This is ideal for development and single-node deployments; swap in the [Hangfire adapter](../Slice.BackgroundJobs.Hangfire/) for durable, distributed execution without changing your job code.

## Dependencies

- **Slice:** `Slice.Core`, `Slice.Modularity`
- **Third-party:** `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Hosting.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`

## Module & registration

`SliceBackgroundJobsModule` registers the in-memory managers (via convention scanning, as singletons) and adds two hosted services: `BackgroundJobWorker` and `RecurringJobScheduler`. Job handlers are registered separately by scanning an assembly with `AddBackgroundJobHandlers`.

```csharp
// Pull in the module (e.g. via [DependsOn(typeof(SliceBackgroundJobsModule))]).

// Register your IBackgroundJob<TArgs> implementations:
services.AddBackgroundJobHandlers(typeof(SendWelcomeEmailJob).Assembly);
```

## Key types

| Type | Kind | Description |
|---|---|---|
| `IBackgroundJob<in TArgs>` | interface | A unit of background work: `Task ExecuteAsync(TArgs args, CancellationToken ct)`. |
| `IBackgroundJobManager` | interface | Enqueues fire-and-forget work: `Task<string> EnqueueAsync<TArgs>(TArgs args, TimeSpan? delay = null)`. |
| `IRecurringJobManager` | interface | Registers interval work: `void AddOrUpdate<TArgs>(string jobId, TArgs args, TimeSpan interval)`. |
| `SliceBackgroundJobsModule` | `SliceModule` | Registers the in-memory managers and hosted services. |
| `BackgroundJobRegistration` | static class | Provides the `AddBackgroundJobHandlers(this IServiceCollection, Assembly)` extension. |
| `InMemoryBackgroundJobManager` | class (`IBackgroundJobManager`, `ISingletonDependency`) | Default manager backed by an unbounded channel. |
| `BackgroundJobWorker` | `BackgroundService` | Drains the channel, executing each job in its own DI scope. |
| `InMemoryRecurringJobManager` | class (`IRecurringJobManager`, `ISingletonDependency`) | In-memory registry of recurring entries. |
| `RecurringJobScheduler` | `BackgroundService` | Polls every second and enqueues due recurring jobs. |

## Usage

Define args, a job, and enqueue it:

```csharp
// 1. Args (any serialisable type)
public sealed record SendWelcomeEmailArgs(Guid UserId, string Email);

// 2. Handler
public sealed class SendWelcomeEmailJob(IEmailSender sender) : IBackgroundJob<SendWelcomeEmailArgs>
{
    public async Task ExecuteAsync(SendWelcomeEmailArgs args, CancellationToken ct)
        => await sender.SendAsync(args.Email, "Welcome!", ct);
}

// 3. Register handlers once at startup
services.AddBackgroundJobHandlers(typeof(SendWelcomeEmailJob).Assembly);

// 4. Enqueue from anywhere that has IBackgroundJobManager
public sealed class RegisterUserHandler(IBackgroundJobManager jobs)
{
    public async Task Handle(Guid userId, string email)
    {
        // immediate
        string jobId = await jobs.EnqueueAsync(new SendWelcomeEmailArgs(userId, email));

        // delayed
        await jobs.EnqueueAsync(new SendWelcomeEmailArgs(userId, email), delay: TimeSpan.FromMinutes(5));
    }
}

// Recurring (e.g. configured at startup)
recurring.AddOrUpdate("nightly-cleanup", new CleanupArgs(), TimeSpan.FromHours(24));
```

## Notes

- **Handler resolution:** `IBackgroundJob<TArgs>` implementations are registered as **transient** by `AddBackgroundJobHandlers`. Each job runs in a freshly created DI scope (`IServiceScopeFactory.CreateScope()`), so scoped dependencies are valid inside `ExecuteAsync`.
- **Managers are singletons** (`InMemoryBackgroundJobManager`, `InMemoryRecurringJobManager`).
- **Not durable:** queued and recurring jobs live only in memory. A process restart loses pending work. Delayed jobs use a fire-and-forget `Task.Delay`. For durability/distribution, use `Slice.BackgroundJobs.Hangfire`, which replaces the managers while keeping `IBackgroundJob<TArgs>` unchanged.
- **Job IDs:** `EnqueueAsync` returns a UUIDv7 (`Guid.CreateVersion7().ToString("N")`).
- **Failures** in a job are caught and logged by `BackgroundJobWorker`; they do not stop the worker.
- **Recurring granularity:** the scheduler polls once per second and fires entries whose `NextRunUtc` has passed.
