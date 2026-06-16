using Microsoft.Extensions.DependencyInjection;

namespace Slice.BackgroundJobs.Hangfire;

/// <summary>
/// The single Hangfire-visible job method. Hangfire serialises the closed generic call + args;
/// at execution time it resolves the matching <see cref="IBackgroundJob{TArgs}"/> from DI.
/// </summary>
public sealed class HangfireJobDispatcher(IServiceProvider serviceProvider)
{
    public Task ExecuteAsync<TArgs>(TArgs args)
        => serviceProvider.GetRequiredService<IBackgroundJob<TArgs>>().ExecuteAsync(args, CancellationToken.None);
}
