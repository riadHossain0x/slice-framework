using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SliceResult = Slice.Core.Results.IResult;

namespace Slice.AspNetCore.MinimalApi;

/// <summary>
/// A self-registering minimal-API endpoint. Implement one per feature (next to its command/query +
/// handler) and map its route(s) in <see cref="Map"/>; <see cref="SliceEndpointExtensions.MapSliceEndpoints"/>
/// discovers and maps every implementation, keeping "a feature is a folder" — no central wiring per feature.
/// </summary>
public interface ISliceEndpoint
{
    void Map(IEndpointRouteBuilder endpoints);
}

/// <summary>
/// Endpoint filter that maps a returned framework <see cref="Slice.Core.Results.IResult"/> to an HTTP
/// result — the minimal-API counterpart of <c>SliceController.ToActionResult</c>. Endpoint delegates can
/// therefore just <c>return sender.SendAsync(request, ct)</c> (which yields a <c>Result&lt;T&gt;</c>); any
/// non-result return value is passed through untouched.
/// </summary>
public sealed class SliceResultEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);
        return result is SliceResult sliceResult ? SliceResults.ToHttpResult(sliceResult) : result;
    }
}

public static class SliceEndpointExtensions
{
    /// <summary>
    /// Discovers every <see cref="ISliceEndpoint"/> in <paramref name="assembly"/> and maps it onto a route
    /// group pre-wired with <see cref="SliceResultEndpointFilter"/> (so result mapping is never forgotten).
    /// Use <paramref name="configure"/> to attach cross-cutting conventions to the whole group — e.g.
    /// <c>g =&gt; g.RequireAuthorization().AddHal().AddResourceVersion()</c> — or a version set. The result
    /// filter is added first (outermost); anything added in <paramref name="configure"/> runs inside it, so
    /// HAL/ETag filters see the raw <c>Result&lt;T&gt;</c> before it is mapped.
    /// </summary>
    public static IEndpointRouteBuilder MapSliceEndpoints(
        this IEndpointRouteBuilder endpoints, Assembly assembly, Action<RouteGroupBuilder>? configure = null)
    {
        var group = endpoints.MapGroup("");
        group.AddEndpointFilter<SliceResultEndpointFilter>();
        configure?.Invoke(group);

        foreach (var type in assembly.GetTypes())
        {
            if (type is { IsClass: true, IsAbstract: false } && typeof(ISliceEndpoint).IsAssignableFrom(type))
                ((ISliceEndpoint)Activator.CreateInstance(type)!).Map(group);
        }

        return endpoints;
    }
}
