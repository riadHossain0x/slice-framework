using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SliceResult = Slice.Core.Results.IResult;

namespace Slice.AspNetCore.ConditionalRequests;

/// <summary>
/// Minimal-API counterpart of <see cref="ResourceVersionResultFilter"/>. On a safe read, if the endpoint's
/// value implements <see cref="IHasResourceVersion"/>, sets a strong <c>ETag</c> from its version so the
/// <see cref="ConditionalRequestMiddleware"/> can answer <c>If-None-Match</c> without hashing the body. It
/// unwraps a success <see cref="Slice.Core.Results.IResult"/>, returns the result unchanged (header-only
/// side effect), and skips when a hypermedia representation is negotiated (its bytes differ, so it falls
/// back to a content hash and the ETag stays representation-consistent).
/// </summary>
public sealed class ResourceVersionEndpointFilter : IEndpointFilter
{
    private const string HalMediaType = "application/hal+json";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);
        var http = context.HttpContext;

        if (HttpMethods.IsGet(http.Request.Method) && !NegotiatesHal(http.Request))
        {
            var value = result is SliceResult { IsSuccess: true } sliceResult ? sliceResult.GetValue() : result;
            if (value is IHasResourceVersion versioned)
                http.SetETag(versioned.ResourceVersion);
        }

        return result;
    }

    private static bool NegotiatesHal(HttpRequest request)
    {
        foreach (var value in request.Headers.Accept)
            if (value is not null && value.Contains(HalMediaType, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}

public static class ResourceVersionEndpointFilterExtensions
{
    /// <summary>Sets a strong ETag from <see cref="IHasResourceVersion"/> on the group's safe reads.</summary>
    public static RouteGroupBuilder AddResourceVersion(this RouteGroupBuilder group)
    {
        group.AddEndpointFilter<ResourceVersionEndpointFilter>();
        return group;
    }

    /// <summary>Sets a strong ETag from <see cref="IHasResourceVersion"/> on a single endpoint's safe read.</summary>
    public static RouteHandlerBuilder WithResourceVersion(this RouteHandlerBuilder builder)
    {
        builder.AddEndpointFilter<ResourceVersionEndpointFilter>();
        return builder;
    }
}
