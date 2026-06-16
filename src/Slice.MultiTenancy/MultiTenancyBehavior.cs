using Slice.Core.Ambient;
using Slice.Mediator;

namespace Slice.MultiTenancy;

/// <summary>
/// Ensures a request runs under a resolved tenant even when there is no HTTP middleware
/// (e.g. background jobs). If a tenant is already ambient (set by the middleware), it's a no-op.
/// </summary>
public sealed class MultiTenancyBehavior<TRequest, TResponse>(
    ITenantResolver resolver, ICurrentTenant currentTenant)
    : IPipelineBehavior<TRequest, TResponse>, IHasPipelineOrder
    where TRequest : IRequest<TResponse>
{
    public int Order => PipelineOrder.MultiTenancy;

    public async Task<TResponse> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (currentTenant.IsAvailable)
            return await next();

        var result = await resolver.ResolveAsync(ct);
        if (!result.Resolved)
            return await next();

        using (currentTenant.Change(result.TenantId))
            return await next();
    }
}
