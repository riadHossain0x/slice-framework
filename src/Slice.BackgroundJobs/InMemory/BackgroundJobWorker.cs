using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Slice.BackgroundJobs.InMemory;

/// <summary>Drains the in-memory job channel, executing each job in its own DI scope.</summary>
public sealed class BackgroundJobWorker(
    InMemoryBackgroundJobManager manager,
    IServiceScopeFactory scopeFactory,
    ILogger<BackgroundJobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in manager.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await item.Execute(scope.ServiceProvider, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background job {JobId} failed", item.Id);
            }
        }
    }
}
