using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Slice.Authorization;
using Slice.Modularity;

namespace Slice.AspNetCore.Hypermedia;

/// <summary>
/// Adds HAL hypermedia to a Slice application: registers the content-negotiated
/// <see cref="HalResourceFilter"/> globally so resource responses gain <c>_links</c>/<c>_embedded</c>
/// when the client asks for <c>application/hal+json</c>. Link contributors are discovered per feature
/// assembly via <see cref="SliceHypermediaExtensions.AddResourceLinkContributors"/>.
/// </summary>
[DependsOn(typeof(SliceAspNetCoreModule), typeof(SliceAuthorizationModule))]
public sealed class SliceHypermediaModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddScoped<HalResourceFilter>();
        context.Services.Configure<MvcOptions>(options => options.Filters.AddService<HalResourceFilter>());
    }
}

public static class SliceHypermediaExtensions
{
    /// <summary>
    /// Scans an assembly for <see cref="IResourceLinkContributor{T}"/> implementations and registers
    /// each against its contributor interface (transient). Mirrors <c>AddRequestHandlers</c>.
    /// </summary>
    public static IServiceCollection AddResourceLinkContributors(this IServiceCollection services, Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (type is not { IsClass: true, IsAbstract: false })
                continue;

            foreach (var contributorInterface in type.GetInterfaces()
                         .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IResourceLinkContributor<>)))
            {
                services.AddTransient(contributorInterface, type);
            }
        }

        return services;
    }
}
