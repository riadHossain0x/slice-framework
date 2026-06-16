using Microsoft.Extensions.DependencyInjection;
using Slice.Application;
using Slice.Mediator;
using Slice.Modularity;

namespace Slice.MultiTenancy;

/// <summary>
/// Multi-tenancy module: registers the ambient <see cref="CurrentTenant"/> (replacing the Core
/// null default), the tenant resolver + claim/header contributors, and the fallback tenancy
/// behavior. Web hosts also call <c>app.UseSliceMultiTenancy()</c>.
/// </summary>
[DependsOn(typeof(SliceApplicationModule))]
public sealed class SliceMultiTenancyModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var services = context.Services;
        services.AddHttpContextAccessor();
        services.AddSliceConventions(typeof(CurrentTenant).Assembly);
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(MultiTenancyBehavior<,>));
    }
}
