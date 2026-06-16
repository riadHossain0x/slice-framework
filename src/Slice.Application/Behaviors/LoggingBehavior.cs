using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Slice.Mediator;

namespace Slice.Application.Behaviors;

/// <summary>Logs each request's name and elapsed time; the outermost behavior.</summary>
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>, IHasPipelineOrder
    where TRequest : IRequest<TResponse>
{
    public int Order => PipelineOrder.Logging;

    public async Task<TResponse> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var requestName = typeof(TRequest).Name;
        var sw = Stopwatch.GetTimestamp();
        logger.LogDebug("Handling {Request}", requestName);
        try
        {
            var response = await next();
            logger.LogInformation("Handled {Request} in {Elapsed}ms",
                requestName, Stopwatch.GetElapsedTime(sw).TotalMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling {Request} after {Elapsed}ms",
                requestName, Stopwatch.GetElapsedTime(sw).TotalMilliseconds);
            throw;
        }
    }
}
