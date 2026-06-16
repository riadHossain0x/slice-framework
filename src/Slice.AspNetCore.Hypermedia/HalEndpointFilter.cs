using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SliceResult = Slice.Core.Results.IResult;

namespace Slice.AspNetCore.Hypermedia;

/// <summary>
/// Minimal-API counterpart of <see cref="HalResourceFilter"/>. When the client negotiates
/// <c>application/hal+json</c>, it renders the endpoint's value as a HAL document (via
/// <see cref="HalDocumentFactory"/>); otherwise it leaves the return value untouched. It unwraps a success
/// <see cref="Slice.Core.Results.IResult"/> so it composes with <c>SliceResultEndpointFilter</c> — add it
/// <em>after</em> the result filter (e.g. via <c>group.AddHal()</c>) so it runs inner and sees the raw result.
/// </summary>
public sealed class HalEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);
        var http = context.HttpContext;

        if (!HalDocumentFactory.AcceptsHal(http.Request))
            return result;

        var value = result;
        if (result is SliceResult sliceResult)
        {
            if (!sliceResult.IsSuccess)
                return result;        // let the result filter map the failure to ProblemDetails
            value = sliceResult.GetValue();
        }

        if (value is null)
            return result;            // 204 / no body — nothing to enrich

        var json = await HalDocumentFactory.BuildJsonAsync(value, http, http.RequestAborted);
        return Results.Content(json, Hal.MediaType, statusCode: StatusCodes.Status200OK);
    }
}

public static class HalEndpointFilterExtensions
{
    /// <summary>Enriches a route group's responses with HAL when <c>application/hal+json</c> is negotiated.</summary>
    public static RouteGroupBuilder AddHal(this RouteGroupBuilder group)
    {
        group.AddEndpointFilter<HalEndpointFilter>();
        return group;
    }

    /// <summary>Enriches a single endpoint's response with HAL when <c>application/hal+json</c> is negotiated.</summary>
    public static RouteHandlerBuilder WithHal(this RouteHandlerBuilder builder)
    {
        builder.AddEndpointFilter<HalEndpointFilter>();
        return builder;
    }
}
