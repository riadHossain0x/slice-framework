using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Slice.BackgroundWorkers;
using Slice.Core.Ambient;
using Slice.Core.DependencyInjection;

namespace Slice.Sample.Crm.Workers;

/// <summary>
/// Periodic worker demo — takes the distributed lock so only one node sweeps at a time.
/// </summary>
public sealed class StaleLeadSweepWorker(ILogger<StaleLeadSweepWorker> logger)
    : IBackgroundWorker, ISingletonDependency
{
    public TimeSpan Period => TimeSpan.FromSeconds(2);

    public async Task DoWorkAsync(IServiceProvider scopedServices, CancellationToken ct)
    {
        var distributedLock = scopedServices.GetRequiredService<IDistributedLock>();
        await using var handle = await distributedLock.TryAcquireAsync("crm:stale-lead-sweep", ct: ct);
        if (handle is null)
            return; // another node holds the sweep lock

        logger.LogInformation("WORKER: stale-lead sweep tick");
    }
}
