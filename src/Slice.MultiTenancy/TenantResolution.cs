using Microsoft.AspNetCore.Http;
using Slice.Core.DependencyInjection;
using Slice.Domain.MultiTenancy;

namespace Slice.MultiTenancy;

public static class TenantConstants
{
    public const string Header = "X-Tenant-Id";
    public const string Claim = MultiTenancyClaims.TenantId;
}

public sealed class TenantResolveResult
{
    public Guid? TenantId { get; set; }
    public bool Resolved => TenantId is not null;
}

/// <summary>A single strategy for discovering the current tenant (claim, header, …).</summary>
public interface ITenantResolveContributor
{
    /// <summary>
    /// Lower runs first, and the first contributor to resolve wins — so this is a trust ordering, not
    /// just an execution order. Signals the caller cannot forge must run before ones they can: the
    /// signed <c>tenant_id</c> claim has to beat the <c>X-Tenant-Id</c> header, or an authenticated
    /// user could put themselves in someone else's tenant just by setting a request header, and every
    /// <c>IMultiTenant</c> query filter downstream would follow them there.
    /// Defaulted so an existing contributor keeps compiling; see <see cref="TenantResolveOrder"/>.
    /// </summary>
    int Order => TenantResolveOrder.Default;

    Task ResolveAsync(TenantResolveResult result, CancellationToken ct);
}

/// <summary>
/// Ordering constants for <see cref="ITenantResolveContributor.Order"/>, mirroring how
/// <c>PipelineOrder</c> makes behavior ordering explicit rather than registration-dependent.
/// </summary>
public static class TenantResolveOrder
{
    /// <summary>The authenticated principal's <c>tenant_id</c> claim — signed, so not forgeable.</summary>
    public const int Claim = 100;

    /// <summary>Anything a host adds that the caller cannot control (subdomain, client certificate, …).</summary>
    public const int Default = 500;

    /// <summary>The <c>X-Tenant-Id</c> request header — caller-supplied, so it must come last.</summary>
    public const int Header = 900;
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
        // Ordered explicitly rather than by DI registration order — which is assembly scan order, and
        // therefore not something to rest a trust decision on.
        foreach (var contributor in contributors.OrderBy(c => c.Order))
        {
            await contributor.ResolveAsync(result, ct);
            if (result.Resolved)
                break;
        }
        return result;
    }
}

/// <summary>
/// Resolves the tenant from the <c>tenant_id</c> claim on the authenticated user. Runs first: the claim
/// is carried by a signed token, so an authenticated caller cannot point it at another tenant.
/// </summary>
public sealed class ClaimTenantResolveContributor(IHttpContextAccessor httpContextAccessor)
    : ITenantResolveContributor, ITransientDependency
{
    public int Order => TenantResolveOrder.Claim;

    public Task ResolveAsync(TenantResolveResult result, CancellationToken ct)
    {
        var value = httpContextAccessor.HttpContext?.User?.FindFirst(TenantConstants.Claim)?.Value;
        if (Guid.TryParse(value, out var tenantId))
            result.TenantId = tenantId;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Resolves the tenant from the <c>X-Tenant-Id</c> request header. Runs last, and only gets a say when
/// nothing more trustworthy resolved — the header is caller-supplied, so it must never override the
/// tenant an authenticated principal's own token asserts.
/// </summary>
public sealed class HeaderTenantResolveContributor(IHttpContextAccessor httpContextAccessor)
    : ITenantResolveContributor, ITransientDependency
{
    public int Order => TenantResolveOrder.Header;

    public Task ResolveAsync(TenantResolveResult result, CancellationToken ct)
    {
        var value = httpContextAccessor.HttpContext?.Request.Headers[TenantConstants.Header].ToString();
        if (Guid.TryParse(value, out var tenantId))
            result.TenantId = tenantId;
        return Task.CompletedTask;
    }
}
