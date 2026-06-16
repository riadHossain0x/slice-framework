using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Slice.Modularity;

namespace Slice.AspNetCore.ConditionalRequests;

/// <summary>
/// Adds HTTP conditional-request support. Registers the <see cref="ResourceVersionResultFilter"/> (which
/// turns a payload's <see cref="IHasResourceVersion"/> into a strong ETag) globally; the host still wires
/// the middleware with <c>UseSliceConditionalRequests()</c> after <c>UseSliceExceptionHandling()</c>.
/// </summary>
[DependsOn(typeof(SliceAspNetCoreModule))]
public sealed class SliceConditionalRequestsModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddOptions<ConditionalRequestOptions>();
        context.Services.AddScoped<ResourceVersionResultFilter>();
        context.Services.Configure<MvcOptions>(options => options.Filters.AddService<ResourceVersionResultFilter>());
    }
}
