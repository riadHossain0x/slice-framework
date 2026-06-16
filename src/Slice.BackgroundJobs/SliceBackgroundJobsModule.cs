using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Slice.BackgroundJobs.InMemory;
using Slice.Modularity;

namespace Slice.BackgroundJobs;

/// <summary>
/// Background-jobs module: registers the in-memory job manager + worker and the recurring
/// scheduler. Replace with the Hangfire/Quartz adapter module for durable, distributed jobs.
/// </summary>
public sealed class SliceBackgroundJobsModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var services = context.Services;
        services.AddSliceConventions(typeof(SliceBackgroundJobsModule).Assembly); // managers (singletons)
        services.AddHostedService<BackgroundJobWorker>();
        services.AddHostedService<RecurringJobScheduler>();
    }
}

public static class BackgroundJobRegistration
{
    /// <summary>Scans an assembly for closed <see cref="IBackgroundJob{TArgs}"/> implementations.</summary>
    public static IServiceCollection AddBackgroundJobHandlers(this IServiceCollection services, Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (type is not { IsClass: true, IsAbstract: false })
                continue;

            foreach (var jobInterface in type.GetInterfaces()
                         .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IBackgroundJob<>)))
            {
                services.AddTransient(jobInterface, type);
            }
        }
        return services;
    }
}
