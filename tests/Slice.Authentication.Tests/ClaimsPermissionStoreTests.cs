using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Slice.Authentication.Tests;

/// <summary>
/// <see cref="ClaimsPermissionStore"/> answers permission checks straight off the request principal.
/// The deny claim is the part worth pinning down: it has to beat a grant no matter which of the
/// user's roles each came from, since a role that denies is only meaningful if it can override.
/// </summary>
public sealed class ClaimsPermissionStoreTests
{
    private sealed class StubHttpContextAccessor(ClaimsPrincipal? user) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = user is null
            ? null
            : new DefaultHttpContext { User = user };
    }

    private static ClaimsPermissionStore StoreFor(params (string Type, string Value)[] claims) =>
        new(new StubHttpContextAccessor(
            new ClaimsPrincipal(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "test"))));

    [Fact]
    public async Task Grants_a_permission_the_principal_carries()
    {
        var store = StoreFor((SliceClaims.Permission, "leads.read"));

        Assert.True(await store.IsGrantedAsync("leads.read"));
    }

    [Fact]
    public async Task Denies_a_permission_the_principal_does_not_carry()
    {
        var store = StoreFor((SliceClaims.Permission, "leads.read"));

        Assert.False(await store.IsGrantedAsync("leads.delete"));
    }

    [Fact]
    public async Task A_deny_claim_overrides_a_matching_grant()
    {
        var store = StoreFor(
            (SliceClaims.Permission, "leads.delete"),
            (SliceClaims.PermissionDeny, "leads.delete"));

        Assert.False(await store.IsGrantedAsync("leads.delete"));
    }

    [Fact]
    public async Task A_deny_claim_leaves_other_permissions_alone()
    {
        var store = StoreFor(
            (SliceClaims.Permission, "leads.read"),
            (SliceClaims.Permission, "leads.delete"),
            (SliceClaims.PermissionDeny, "leads.delete"));

        Assert.True(await store.IsGrantedAsync("leads.read"));
        Assert.False(await store.IsGrantedAsync("leads.delete"));
    }

    [Fact]
    public async Task Grants_nothing_when_there_is_no_request()
    {
        var store = new ClaimsPermissionStore(new StubHttpContextAccessor(user: null));

        Assert.False(await store.IsGrantedAsync("leads.read"));
    }
}
