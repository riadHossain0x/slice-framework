using global::Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Slice.BackgroundJobs.Hangfire;

public static class HangfireRegistration
{
    /// <summary>
    /// Swaps the in-memory background-job adapter for Hangfire. Supply storage/server config via
    /// <paramref name="configure"/> (e.g. <c>cfg.UseInMemoryStorage()</c> or a SQL storage), then
    /// add a Hangfire server with <c>services.AddHangfireServer()</c> in the host.
    /// Durable, distributed execution; jobs implement the same <see cref="IBackgroundJob{TArgs}"/>.
    /// </summary>
    public static IServiceCollection AddSliceHangfire(
        this IServiceCollection services, Action<IGlobalConfiguration> configure)
    {
        services.AddHangfire(configure);
        services.AddTransient<HangfireJobDispatcher>();

        // Replace the in-memory managers (if registered) with the Hangfire-backed ones.
        services.RemoveAll<IBackgroundJobManager>();
        services.RemoveAll<IRecurringJobManager>();
        services.AddTransient<IBackgroundJobManager, HangfireBackgroundJobManager>();
        services.AddTransient<IRecurringJobManager, HangfireRecurringJobManager>();
        return services;
    }
}
