using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Slice.Core.DependencyInjection;

namespace Slice.BackgroundJobs.InMemory;

internal sealed class RecurringEntry
{
    public required TimeSpan Interval { get; init; }
    public required Func<Task> Enqueue { get; init; }
    public DateTime NextRunUtc { get; set; }
}

/// <summary>In-memory recurring registry; <see cref="RecurringJobScheduler"/> fires due jobs.</summary>
public sealed class InMemoryRecurringJobManager(IBackgroundJobManager jobManager)
    : IRecurringJobManager, ISingletonDependency
{
    internal ConcurrentDictionary<string, RecurringEntry> Entries { get; } = new();

    public void AddOrUpdate<TArgs>(string jobId, TArgs args, TimeSpan interval)
    {
        Entries[jobId] = new RecurringEntry
        {
            Interval = interval,
            Enqueue = () => jobManager.EnqueueAsync(args),
            NextRunUtc = DateTime.UtcNow.Add(interval)
        };
    }
}

public sealed class RecurringJobScheduler(InMemoryRecurringJobManager manager) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            foreach (var entry in manager.Entries.Values)
            {
                if (entry.NextRunUtc <= now)
                {
                    entry.NextRunUtc = now.Add(entry.Interval);
                    await entry.Enqueue();
                }
            }

            try { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }
}
