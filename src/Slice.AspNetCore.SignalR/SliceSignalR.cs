using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Slice.AspNetCore;
using Slice.Modularity;

namespace Slice.AspNetCore.SignalR;

/// <summary>Adds SignalR (real-time hubs) to a Slice application.</summary>
[DependsOn(typeof(SliceAspNetCoreModule))]
public sealed class SliceSignalRModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
        => context.Services.AddSignalR();
}

public static class SliceSignalRExtensions
{
    /// <summary>Registers SignalR explicitly (use when not composing via <see cref="SliceSignalRModule"/>).</summary>
    public static ISignalRServerBuilder AddSliceSignalR(this IServiceCollection services, Action<HubOptions>? configure = null)
        => configure is null ? services.AddSignalR() : services.AddSignalR(configure);

    /// <summary>Maps a Slice hub to a route (thin wrapper over <c>MapHub</c> for symmetry).</summary>
    public static HubEndpointConventionBuilder MapSliceHub<THub>(this IEndpointRouteBuilder endpoints, string pattern)
        where THub : Hub
        => endpoints.MapHub<THub>(pattern);
}
