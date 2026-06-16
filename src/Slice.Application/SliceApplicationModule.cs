using Microsoft.Extensions.DependencyInjection;
using Slice.Application.Behaviors;
using Slice.Core.Ambient;
using Slice.Mediator;
using Slice.Modularity;

namespace Slice.Application;

/// <summary>
/// Core application module: registers the standard pipeline behaviors (outermost-first:
/// Logging → Validation) and the ambient core services. Feature modules depend on this.
/// </summary>
public sealed class SliceApplicationModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var services = context.Services;

        // Behavior order = registration order (outermost first). Later phases insert
        // tenancy/authorization/unit-of-work between these.
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(UnitOfWorkBehavior<,>));

        // Ambient core services (Clock, GuidGenerator) by DI conventions.
        services.AddSliceConventions(typeof(Clock).Assembly);
    }
}
