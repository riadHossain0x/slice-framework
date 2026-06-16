using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Slice.Core.DependencyInjection;

namespace Slice.BackgroundJobs.InMemory;

/// <summary>A queued job: resolves its handler in a fresh scope and runs it.</summary>
internal sealed record JobWorkItem(string Id, Func<IServiceProvider, CancellationToken, Task> Execute);

/// <summary>
/// Default <see cref="IBackgroundJobManager"/>: an in-process channel drained by
/// <see cref="BackgroundJobWorker"/>. Suitable for dev/single-node; swap in the Hangfire/Quartz
/// adapter for durable, distributed execution.
/// </summary>
public sealed class InMemoryBackgroundJobManager : IBackgroundJobManager, ISingletonDependency
{
    private readonly Channel<JobWorkItem> _channel =
        Channel.CreateUnbounded<JobWorkItem>(new UnboundedChannelOptions { SingleReader = true });

    internal ChannelReader<JobWorkItem> Reader => _channel.Reader;

    public Task<string> EnqueueAsync<TArgs>(TArgs args, TimeSpan? delay = null)
    {
        var id = Guid.CreateVersion7().ToString("N");
        var item = new JobWorkItem(id, (sp, ct) =>
            sp.GetRequiredService<IBackgroundJob<TArgs>>().ExecuteAsync(args, ct));

        if (delay is { } d && d > TimeSpan.Zero)
            _ = DelayThenQueueAsync(item, d);
        else
            _channel.Writer.TryWrite(item);

        return Task.FromResult(id);
    }

    private async Task DelayThenQueueAsync(JobWorkItem item, TimeSpan delay)
    {
        await Task.Delay(delay);
        _channel.Writer.TryWrite(item);
    }
}
