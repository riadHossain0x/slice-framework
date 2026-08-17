using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Slice.Domain.MultiTenancy;
using Slice.MultiTenancy;

namespace Slice.Data.Tests;

/// <summary>
/// Contributor order is a trust ordering, not just an execution order: the resolver stops at the first
/// contributor that resolves, so whichever runs first decides the tenant. The signed <c>tenant_id</c>
/// claim must beat the caller-supplied <c>X-Tenant-Id</c> header — otherwise an authenticated user
/// moves themselves into another tenant with one request header, and every <c>IMultiTenant</c> query
/// filter downstream follows them there.
/// </summary>
public sealed class TenantResolverOrderTests
{
    private static IHttpContextAccessor Request(Guid? claimTenant, Guid? headerTenant)
    {
        var context = new DefaultHttpContext();

        if (claimTenant is { } claim)
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(MultiTenancyClaims.TenantId, claim.ToString())], "test"));

        if (headerTenant is { } header)
            context.Request.Headers[TenantConstants.Header] = header.ToString();

        return new HttpContextAccessor { HttpContext = context };
    }

    /// <summary>Deliberately registered header-first, so a passing test can't be an artifact of order.</summary>
    private static TenantResolver ResolverFor(IHttpContextAccessor accessor) =>
        new([new HeaderTenantResolveContributor(accessor), new ClaimTenantResolveContributor(accessor)]);

    [Fact]
    public async Task The_signed_claim_wins_over_a_conflicting_header()
    {
        var ownTenant = Guid.NewGuid();
        var someoneElses = Guid.NewGuid();

        var result = await ResolverFor(Request(claimTenant: ownTenant, headerTenant: someoneElses)).ResolveAsync();

        Assert.Equal(ownTenant, result.TenantId);
    }

    [Fact]
    public async Task The_header_still_resolves_when_there_is_no_claim()
    {
        // Platform-tier operators and unauthenticated tenant routing both rely on this.
        var tenant = Guid.NewGuid();

        var result = await ResolverFor(Request(claimTenant: null, headerTenant: tenant)).ResolveAsync();

        Assert.Equal(tenant, result.TenantId);
    }

    [Fact]
    public async Task Nothing_resolves_when_neither_signal_is_present()
    {
        var result = await ResolverFor(Request(claimTenant: null, headerTenant: null)).ResolveAsync();

        Assert.False(result.Resolved);
        Assert.Null(result.TenantId);
    }

    [Fact]
    public void The_claim_contributor_is_ordered_ahead_of_the_header_contributor()
    {
        var accessor = Request(null, null);

        Assert.True(new ClaimTenantResolveContributor(accessor).Order
                  < new HeaderTenantResolveContributor(accessor).Order);
    }
}
