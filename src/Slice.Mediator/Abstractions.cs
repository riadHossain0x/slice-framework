namespace Slice.Mediator;

/// <summary>A void response marker (CQRS commands that return nothing meaningful).</summary>
public readonly record struct Unit
{
    public static readonly Unit Value = default;
}

/// <summary>A request (command or query) that produces a <typeparamref name="TResponse"/>.</summary>
public interface IRequest<out TResponse>;

/// <summary>A request that produces no value.</summary>
public interface IRequest : IRequest<Unit>;

/// <summary>Handles a single request type. Exactly one handler per request.</summary>
public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, CancellationToken ct);
}

/// <summary>Convenience base for void-returning handlers.</summary>
public interface IRequestHandler<in TRequest> : IRequestHandler<TRequest, Unit> where TRequest : IRequest<Unit>;

/// <summary>The continuation that invokes the next behavior (or the handler).</summary>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

/// <summary>Wraps request handling with cross-cutting logic. Chained outermost-first.</summary>
public interface IPipelineBehavior<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct);
}

/// <summary>Dispatches a request through the pipeline to its handler. The engine seam.</summary>
public interface ISender
{
    Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken ct = default);
}
