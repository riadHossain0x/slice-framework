using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Slice.BackgroundWorkers;
using Slice.Core.Ambient;
using Slice.Core.DependencyInjection;

namespace SliceWorker.Workers;

/// <summary>
/// A periodic background worker. <see cref="DoWorkAsync"/> runs on its own <see cref="Period"/> with a
/// fresh DI scope — resolve scoped services (a DbContext, repositories, <c>ISender</c>) from
/// <paramref name="scopedServices"/>. Take <see cref="IDistributedLock"/> for single-runner work across
/// nodes (no-op locally; register Redis for real coordination).
/// </summary>
public sealed class SweepWorker(ILogger<SweepWorker> logger) : IBackgroundWorker, ISingletonDependency
{
    public TimeSpan Period => TimeSpan.FromSeconds(10);

    public async Task DoWorkAsync(IServiceProvider scopedServices, CancellationToken ct)
    {
        var clock = scopedServices.GetRequiredService<IClock>();
        logger.LogInformation("WORKER: sweep tick at {Now:O}", clock.Now);
        await Task.CompletedTask;
    }
}
