using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Slice.Application;
using Slice.Modularity;

namespace Slice.BackgroundWorkers;

/// <summary>A recurring worker that ticks every <see cref="Period"/> with a fresh DI scope.</summary>
public interface IBackgroundWorker
{
    TimeSpan Period { get; }
    Task DoWorkAsync(IServiceProvider scopedServices, CancellationToken ct);
}

/// <summary>Drives every registered <see cref="IBackgroundWorker"/> on its own period.</summary>
public sealed class BackgroundWorkerManager(
    IEnumerable<IBackgroundWorker> workers,
    IServiceScopeFactory scopeFactory,
    ILogger<BackgroundWorkerManager> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.WhenAll(workers.Select(w => RunAsync(w, stoppingToken)));

    private async Task RunAsync(IBackgroundWorker worker, CancellationToken ct)
    {
        var name = worker.GetType().Name;
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(worker.Period, ct); }
            catch (TaskCanceledException) { break; }

            try
            {
                using var scope = scopeFactory.CreateScope();
                await worker.DoWorkAsync(scope.ServiceProvider, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background worker {Worker} failed", name);
            }
        }
    }
}

[DependsOn(typeof(SliceApplicationModule))]
public sealed class SliceBackgroundWorkersModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
        => context.Services.AddHostedService<BackgroundWorkerManager>();
}
