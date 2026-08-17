using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Slice.Application.Results;
using Slice.AspNetCore.Http;
using Slice.Core.Results;
using Slice.Domain.Exceptions;

namespace Slice.AspNetCore.Http;

/// <summary>
/// Turns a mapped <see cref="ProblemDetails"/> into an HTTP response. Replaceable so a host can
/// answer differently per request — most commonly a server-rendered app that wants a 401 to
/// challenge/redirect to its login page and a 403 to render an access-denied view, while still
/// returning JSON to its API clients.
///
/// Register your own later than <c>SliceAspNetCoreModule</c> and it wins (single-instance
/// resolution, last registration wins — the same idiom <c>IPermissionStore</c> uses).
/// </summary>
public interface ISliceProblemResponseWriter
{
    Task WriteAsync(HttpContext context, ProblemDetails problem);
}

/// <summary>
/// Default: the problem document as JSON, always. Registered explicitly by
/// <c>SliceAspNetCoreModule</c> — this assembly carries no conventional registrar, so a DI marker
/// interface here would silently register nothing.
/// </summary>
public sealed class JsonProblemResponseWriter : ISliceProblemResponseWriter
{
    public Task WriteAsync(HttpContext context, ProblemDetails problem)
    {
        context.Response.StatusCode = problem.Status!.Value;
        return context.Response.WriteAsJsonAsync(problem, problem.GetType());
    }
}

/// <summary>
/// Last-resort handler: maps domain exceptions and pipeline failures to ProblemDetails,
/// and anything else to a 500. Expected business outcomes should travel as <see cref="Result"/>,
/// not exceptions — this is the safety net.
/// </summary>
public sealed class SliceExceptionMiddleware(RequestDelegate next, ILogger<SliceExceptionMiddleware> logger)
{
    // Resolved per request rather than injected into the constructor: middleware is a singleton, so
    // constructor injection would pin a host's replacement to that lifetime too.
    public async Task InvokeAsync(HttpContext context, ISliceProblemResponseWriter writer)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            var error = Map(ex);
            if (error.Type == ErrorType.Unexpected)
                logger.LogError(ex, "Unhandled exception");

            await writer.WriteAsync(context, ProblemDetailsMapper.ToProblemDetails(error));
        }
    }

    private static Error Map(Exception ex) => ex switch
    {
        AppValidationException e => Error.Validation(e.Code, e.Message),
        EntityNotFoundException e => Error.NotFound(e.Code, e.Message),
        BusinessRuleException e => Error.Conflict(e.Code, e.Message),
        SlicePipelineException e => e.Error,
        _ => Error.Unexpected("Server:Unexpected", "An unexpected error occurred.")
    };
}

public static class SliceExceptionMiddlewareExtensions
{
    public static IApplicationBuilder UseSliceExceptionHandling(this IApplicationBuilder app)
        => app.UseMiddleware<SliceExceptionMiddleware>();
}
