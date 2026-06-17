using Microsoft.Extensions.DependencyInjection;
using Slice.BackgroundWorkers;
using Slice.Mediator.Default;
using Slice.Modularity;

namespace SliceWorker;

/// <summary>
/// Root module for the worker host. <see cref="SliceBackgroundWorkersModule"/> registers the hosted
/// service that drives every <c>IBackgroundWorker</c>; the convention scan discovers the workers in
/// this assembly. Add EF / messaging modules to <c>[DependsOn]</c> as your workers need them.
/// </summary>
[DependsOn(typeof(SliceBackgroundWorkersModule))]
public sealed class WorkerModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var assembly = typeof(WorkerModule).Assembly;
        context.Services.AddSliceMediator();          // so workers can resolve ISender for commands/queries
        context.Services.AddSliceConventions(assembly); // discovers IBackgroundWorker (ISingletonDependency)
    }
}
