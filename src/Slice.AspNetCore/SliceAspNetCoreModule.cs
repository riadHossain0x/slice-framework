using Microsoft.Extensions.DependencyInjection;
using Slice.Application;
using Slice.AspNetCore.Http;
using Slice.Modularity;

namespace Slice.AspNetCore;

/// <summary>
/// Web module: enables MVC controllers (slice-local controllers are discovered by the standard
/// application-part scan of referenced assemblies). Depends on the application pipeline.
/// </summary>
[DependsOn(typeof(SliceApplicationModule))]
public sealed class SliceAspNetCoreModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddControllers();
        context.Services.AddProblemDetails();

        // Plain Add, not TryAdd: a host replaces this by registering its own after the module, which
        // is the framework's usual last-registration-wins seam. See ISliceProblemResponseWriter.
        context.Services.AddSingleton<ISliceProblemResponseWriter, JsonProblemResponseWriter>();
    }
}
