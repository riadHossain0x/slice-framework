using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Slice.AspNetCore.ConditionalRequests;

/// <summary>
/// Sets a strong <c>ETag</c> from a payload's <see cref="IHasResourceVersion"/> on safe reads, so the
/// middleware can answer <c>If-None-Match</c> without hashing the body. Runs early (low order) so it sees
/// the typed result before any representation transform (e.g. HAL). It deliberately skips when the client
/// negotiates a hypermedia representation — that response's bytes differ, so it falls back to a content
/// hash and the ETag stays representation-consistent.
/// </summary>
public sealed class ResourceVersionResultFilter : IAsyncResultFilter, IOrderedFilter
{
    private const string HalMediaType = "application/hal+json";

    public int Order => int.MinValue + 100;

    public Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is ObjectResult { Value: IHasResourceVersion versioned }
            && HttpMethods.IsGet(context.HttpContext.Request.Method)
            && !NegotiatesHal(context.HttpContext.Request))
        {
            context.HttpContext.SetETag(versioned.ResourceVersion);
        }

        return next();
    }

    private static bool NegotiatesHal(HttpRequest request)
    {
        foreach (var value in request.Headers.Accept)
            if (value is not null && value.Contains(HalMediaType, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
