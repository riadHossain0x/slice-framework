using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Slice.Authentication;

/// <summary>
/// The <see cref="IUserClaimsPrincipalFactory{TUser}"/> used for <see cref="SliceUser"/>. Registered
/// by <see cref="SliceAuthenticationModule"/> in place of the role-aware factory that
/// <c>.AddRoles&lt;SliceRole&gt;()</c> installs by default.
///
/// Why it has to exist: the stock <c>UserClaimsPrincipalFactory&lt;TUser, TRole&gt;</c> expands the
/// signed-in user's role NAMES back into roles via <c>RoleManager.FindByNameAsync</c> — a bare,
/// non-tenant-scoped <c>NormalizedName</c> lookup — and merges that role's claims into the principal.
/// Once role names repeat across tenants (every tenant gets its own "Admin", "Manager", … — see
/// docs/multitenancy.md), that lookup is ambiguous: the store query has no explicit ordering, so it
/// returns whichever same-named role happens to sort first and merges an arbitrary OTHER tenant's
/// claims into the principal. <c>SignInManager.SignInAsync</c>/<c>SignInWithClaimsAsync</c> — which
/// <see cref="SliceAuthenticationModule"/> wires up via <c>.AddSignInManager()</c> — goes through this
/// factory, so leaving the default in place would make cookie-based sign-in the leaky path even in an
/// app that resolves roles correctly everywhere else.
///
/// This deliberately derives from the non-role-aware one-type-parameter base and re-adds the role and
/// permission claims itself, resolving them through <see cref="ITenantRoleAssigner"/> against the
/// user's actual role links — the same source <see cref="ConnectController"/> uses for an access
/// token, so the two paths grant the same thing.
///
/// The <c>permission</c>, <c>permission_deny</c>, <c>data_scope</c> and <c>tenant_id</c> claims are
/// identical on both paths, so permission-based authorization behaves the same either way. Role claims
/// carry the same values under a different claim TYPE: here it is
/// <c>IdentityOptions.ClaimsIdentity.RoleClaimType</c> (what <c>ClaimsPrincipal.IsInRole</c> and
/// <c>[Authorize(Roles = ...)]</c> read for a cookie principal), while the token uses OpenIddict's
/// <c>role</c>. Both work in their own pipeline; only code comparing raw claim types across the two
/// will notice.
/// </summary>
public class TenantSafeUserClaimsPrincipalFactory(
    UserManager<SliceUser> userManager,
    RoleManager<SliceRole> roleManager,
    ITenantRoleAssigner tenantRoleAssigner,
    IOptions<IdentityOptions> optionsAccessor)
    : UserClaimsPrincipalFactory<SliceUser>(userManager, optionsAccessor)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(SliceUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        if (user.TenantId is { } tenantId)
            identity.AddClaim(new Claim(SliceClaims.TenantId, tenantId.ToString()));

        var roles = await tenantRoleAssigner.GetRolesOfUserAsync(user);
        foreach (var role in roles.Where(r => r.Name is not null))
            identity.AddClaim(new Claim(Options.ClaimsIdentity.RoleClaimType, role.Name!));

        foreach (var claim in await RoleClaimsAsync(roles))
            identity.AddClaim(claim);

        return identity;
    }

    /// <summary>
    /// The deduplicated permission/deny/data-scope claims carried by the given roles. Deny claims are
    /// kept as-is rather than being subtracted here: <see cref="ClaimsPermissionStore"/> applies the
    /// precedence (a deny beats a grant), so both must reach the principal for it to do that.
    /// </summary>
    private async Task<IEnumerable<Claim>> RoleClaimsAsync(IEnumerable<SliceRole> roles)
    {
        var collected = new HashSet<(string Type, string Value)>();

        foreach (var role in roles)
            foreach (var claim in await roleManager.GetClaimsAsync(role))
                if (claim.Type is SliceClaims.Permission or SliceClaims.PermissionDeny or SliceClaims.DataScope)
                    collected.Add((claim.Type, claim.Value));

        return collected.Select(c => new Claim(c.Type, c.Value));
    }
}
