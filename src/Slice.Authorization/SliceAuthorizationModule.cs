using Microsoft.Extensions.DependencyInjection;
using Slice.Application;
using Slice.Mediator;
using Slice.Modularity;

namespace Slice.Authorization;

/// <summary>
/// Authorization module: registers the permission definition manager, checker, default
/// configuration-backed store, and the authorization pipeline behavior. Permission
/// definition providers are discovered from feature modules by convention.
/// </summary>
[DependsOn(typeof(SliceApplicationModule))]
public sealed class SliceAuthorizationModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddSliceConventions(typeof(SliceAuthorizationModule).Assembly);
        context.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));
    }
}
