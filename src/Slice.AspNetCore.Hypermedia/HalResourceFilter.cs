using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Slice.AspNetCore.Hypermedia;

/// <summary>
/// MVC result filter that turns a controller's resource payload into a HAL document — but only when the
/// client negotiates <c>application/hal+json</c>. Plain <c>application/json</c> callers get the exact same
/// body as before (non-breaking). The HAL document is produced by <see cref="HalDocumentFactory"/>, shared
/// with the minimal-API <c>HalEndpointFilter</c>.
/// </summary>
public sealed class HalResourceFilter : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is not ObjectResult { Value: { } value } objectResult
            || !HalDocumentFactory.AcceptsHal(context.HttpContext.Request))
        {
            await next();
            return;
        }

        var json = await HalDocumentFactory.BuildJsonAsync(value, context.HttpContext, context.HttpContext.RequestAborted);

        context.Result = new ContentResult
        {
            Content = json,
            ContentType = Hal.MediaType,
            StatusCode = objectResult.StatusCode ?? StatusCodes.Status200OK
        };

        await next();
    }
}
