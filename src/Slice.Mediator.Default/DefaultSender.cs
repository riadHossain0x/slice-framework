using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Slice.Mediator.Default;

/// <summary>
/// Default <see cref="ISender"/>. Delegates to <see cref="RequestPipeline"/>, which resolves the
/// handler + ordered pipeline behaviors for a request's runtime type and folds them into a single
/// call chain (cached per request type — the only per-request reflection is a dictionary lookup).
/// </summary>
public sealed class DefaultSender(IServiceProvider serviceProvider) : ISender
{
    public Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
        => RequestPipeline.InvokeAsync(request, serviceProvider, ct);
}

/// <summary>
/// Reusable request-pipeline runner. Shared by the default sender and the MediatR adapter so both
/// engines run the identical Slice handler + behavior chain.
/// </summary>
public static class RequestPipeline
{
    private static readonly ConcurrentDictionary<Type, object> Wrappers = new();

    public static Task<TResponse> InvokeAsync<TResponse>(
        IRequest<TResponse> request, IServiceProvider serviceProvider, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var wrapper = (RequestHandlerWrapper<TResponse>)Wrappers.GetOrAdd(
            request.GetType(),
            reqType => Activator.CreateInstance(
                typeof(RequestHandlerWrapperImpl<,>).MakeGenericType(reqType, typeof(TResponse)))!);

        return wrapper.HandleAsync(request, serviceProvider, ct);
    }

    // ── Non-generic entry (boxed result) — used by engine adapters that hold the request as object ──
    private static readonly ConcurrentDictionary<Type, Func<object, IServiceProvider, CancellationToken, Task<object?>>> BoxedInvokers = new();

    public static Task<object?> InvokeAsync(object request, IServiceProvider serviceProvider, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var invoker = BoxedInvokers.GetOrAdd(request.GetType(), BuildBoxedInvoker);
        return invoker(request, serviceProvider, ct);
    }

    private static Func<object, IServiceProvider, CancellationToken, Task<object?>> BuildBoxedInvoker(Type requestType)
    {
        var iface = Array.Find(requestType.GetInterfaces(),
            i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>))
            ?? throw new InvalidOperationException($"'{requestType}' does not implement IRequest<TResponse>.");

        var responseType = iface.GetGenericArguments()[0];
        var method = typeof(RequestPipeline)
            .GetMethod(nameof(InvokeBoxed), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(responseType);

        return method.CreateDelegate<Func<object, IServiceProvider, CancellationToken, Task<object?>>>();
    }

    private static async Task<object?> InvokeBoxed<TResponse>(object request, IServiceProvider serviceProvider, CancellationToken ct)
        => await InvokeAsync((IRequest<TResponse>)request, serviceProvider, ct);
}

internal abstract class RequestHandlerWrapper<TResponse>
{
    public abstract Task<TResponse> HandleAsync(object request, IServiceProvider sp, CancellationToken ct);
}

internal sealed class RequestHandlerWrapperImpl<TRequest, TResponse> : RequestHandlerWrapper<TResponse>
    where TRequest : IRequest<TResponse>
{
    public override Task<TResponse> HandleAsync(object request, IServiceProvider sp, CancellationToken ct)
    {
        var typed = (TRequest)request;
        var handler = sp.GetRequiredService<IRequestHandler<TRequest, TResponse>>();

        RequestHandlerDelegate<TResponse> handlerCall = () => handler.HandleAsync(typed, ct);

        // Order behaviors by ascending Order (lowest = outermost); unordered run innermost.
        // Reverse so the first (outermost) ends up wrapping last in the fold.
        var pipeline = sp.GetServices<IPipelineBehavior<TRequest, TResponse>>()
            .OrderBy(b => (b as IHasPipelineOrder)?.Order ?? PipelineOrder.Default)
            .Reverse()
            .Aggregate(handlerCall, (next, behavior) => () => behavior.HandleAsync(typed, next, ct));

        return pipeline();
    }
}
