using Microsoft.AspNetCore.Http;
using Slice.Core.DependencyInjection;

namespace Slice.MultiTenancy;

public static class TenantConstants
{
    public const string Header = "X-Tenant-Id";
    public const string Claim = "tenant_id";
}

public sealed class TenantResolveResult
{
    public Guid? TenantId { get; set; }
    public bool Resolved => TenantId is not null;
}

/// <summary>A single strategy for discovering the current tenant (claim, header, …).</summary>
public interface ITenantResolveContributor
{
    Task ResolveAsync(TenantResolveResult result, CancellationToken ct);
}

/// <summary>Runs the registered contributors in order; first to resolve wins.</summary>
public interface ITenantResolver
{
    Task<TenantResolveResult> ResolveAsync(CancellationToken ct = default);
}

public sealed class TenantResolver(IEnumerable<ITenantResolveContributor> contributors)
    : ITenantResolver, ITransientDependency
{
    public async Task<TenantResolveResult> ResolveAsync(CancellationToken ct = default)
    {
        var result = new TenantResolveResult();
        foreach (var contributor in contributors)
        {
            await contributor.ResolveAsync(result, ct);
            if (result.Resolved)
                break;
        }
        return result;
    }
}

/// <summary>Resolves the tenant from the <c>tenant_id</c> claim on the authenticated user.</summary>
public sealed class ClaimTenantResolveContributor(IHttpContextAccessor httpContextAccessor)
    : ITenantResolveContributor, ITransientDependency
{
    public Task ResolveAsync(TenantResolveResult result, CancellationToken ct)
    {
        var value = httpContextAccessor.HttpContext?.User?.FindFirst(TenantConstants.Claim)?.Value;
        if (Guid.TryParse(value, out var tenantId))
            result.TenantId = tenantId;
        return Task.CompletedTask;
    }
}

/// <summary>Resolves the tenant from the <c>X-Tenant-Id</c> request header.</summary>
public sealed class HeaderTenantResolveContributor(IHttpContextAccessor httpContextAccessor)
    : ITenantResolveContributor, ITransientDependency
{
    public Task ResolveAsync(TenantResolveResult result, CancellationToken ct)
    {
        var value = httpContextAccessor.HttpContext?.Request.Headers[TenantConstants.Header].ToString();
        if (Guid.TryParse(value, out var tenantId))
            result.TenantId = tenantId;
        return Task.CompletedTask;
    }
}
