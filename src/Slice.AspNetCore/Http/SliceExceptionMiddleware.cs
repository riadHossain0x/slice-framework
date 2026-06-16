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
/// Last-resort handler: maps domain exceptions and pipeline failures to ProblemDetails,
/// and anything else to a 500. Expected business outcomes should travel as <see cref="Result"/>,
/// not exceptions — this is the safety net.
/// </summary>
public sealed class SliceExceptionMiddleware(RequestDelegate next, ILogger<SliceExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
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

            var problem = ProblemDetailsMapper.ToProblemDetails(error);
            context.Response.StatusCode = problem.Status!.Value;
            await context.Response.WriteAsJsonAsync(problem, problem.GetType());
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
