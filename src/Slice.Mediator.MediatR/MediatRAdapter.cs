using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Slice.Mediator.Default;

namespace Slice.Mediator.MediatR;

/// <summary>
/// A non-generic MediatR request that carries any Slice request, so MediatR can be the dispatch
/// engine while Slice handlers/behaviors run unchanged (no open-generic handler registration needed).
/// </summary>
public sealed class SliceRequestEnvelope(object request) : global::MediatR.IRequest<object?>
{
    public object Request { get; } = request;
}

/// <summary>The single MediatR handler — runs the Slice pipeline for the wrapped request.</summary>
public sealed class SliceRequestEnvelopeHandler(IServiceProvider serviceProvider)
    : global::MediatR.IRequestHandler<SliceRequestEnvelope, object?>
{
    public Task<object?> Handle(SliceRequestEnvelope envelope, CancellationToken ct)
        => RequestPipeline.InvokeAsync(envelope.Request, serviceProvider, ct);
}

/// <summary><see cref="ISender"/> backed by MediatR.</summary>
public sealed class MediatRSender(global::MediatR.ISender mediator) : ISender
{
    public async Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new SliceRequestEnvelope(request), ct);
        return (TResponse)result!;
    }
}

public static class MediatRMediatorRegistration
{
    /// <summary>
    /// Uses MediatR as the dispatch engine instead of the built-in default. Product code keeps
    /// using <see cref="ISender"/>/<see cref="IRequest{TResponse}"/>; Slice handlers and ordered
    /// behaviors run identically inside MediatR's pipeline.
    /// </summary>
    public static IServiceCollection AddSliceMediatorMediatR(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(MediatRSender).Assembly));

        services.RemoveAll<ISender>();
        services.AddTransient<ISender, MediatRSender>();
        return services;
    }
}
