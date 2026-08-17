using System.Collections.Immutable;
using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Slice.Authentication;

/// <summary>
/// Outcome of resolving a submitted username/email + password to account rows.
/// <see cref="Matches"/> holds every account whose password checked out — normally zero or one;
/// more than one means the same credentials are valid in several tenants and the caller has to say
/// which. <see cref="ExceededCandidateLimit"/> means there were more accounts sharing the identifier
/// than <see cref="SliceAuthOptions.MaxLoginCandidates"/> allows checking in one request, so no
/// password was verified at all.
/// </summary>
public sealed record LoginCandidates(IReadOnlyList<SliceUser> Matches, bool ExceededCandidateLimit = false)
{
    public static readonly LoginCandidates TooMany = new([], ExceededCandidateLimit: true);
}

/// <summary>OAuth2/OIDC token endpoint. Handles the password and refresh_token grants.</summary>
public class ConnectController(
    UserManager<SliceUser> userManager,
    RoleManager<SliceRole> roleManager,
    ITenantRoleAssigner tenantRoleAssigner,
    SliceAuthOptions options)
    : ControllerBase
{
    /// <summary>Exposed for derived classes overriding <see cref="ResolveUserAsync"/>.</summary>
    protected UserManager<SliceUser> UserManager { get; } = userManager;

    /// <summary>Exposed for derived classes overriding <see cref="ResolveUserAsync"/>.</summary>
    protected SliceAuthOptions Options { get; } = options;

    /// <summary>Exposed so derived classes can build the same <c>invalid_grant</c> response shape.</summary>
    protected ForbidResult Reject(string description) => Forbid(
        authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
        properties: new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
        }));

    [HttpPost("~/connect/token"), Produces("application/json")]
    public async Task<IActionResult> Exchange()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenIddict request cannot be retrieved.");

        if (request.IsPasswordGrantType())
        {
            var candidates = await ResolveUserAsync(request.Username!, request.Password!, request.GetParameter("tenant_id")?.ToString());

            // Deliberately the same generic message as a failed password. The cap is reached before
            // any password is checked, so saying "too many accounts share this identifier" would tell
            // an unauthenticated caller how widely an address is used across tenants — the very
            // enumeration RejectAmbiguousAsync refuses to do. That path can afford to be specific
            // because the caller has already proven a valid credential for every candidate; this one
            // has proven nothing. A caller that legitimately needs to get past the cap sends
            // 'tenant_id' (which narrows the query before the cap applies), or the host scopes
            // candidates to an already-resolved tenant by overriding ResolveUserAsync.
            if (candidates.ExceededCandidateLimit)
                return Reject("The username/password couple is invalid.");

            var matches = candidates.Matches;
            if (matches.Count > 1)
                return await RejectAmbiguousAsync(matches);
            if (matches.Count == 0)
                return Reject("The username/password couple is invalid.");

            if (await ValidateBeforeSignInAsync(matches[0]) is { } blocked)
                return blocked;

            return SignIn(await CreatePrincipalAsync(matches[0], request.GetScopes()),
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (request.IsRefreshTokenGrantType())
        {
            var auth = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            var subject = auth.Principal?.GetClaim(Claims.Subject);
            var user = subject is null ? null : await userManager.FindByIdAsync(subject);
            if (user is null)
                return Reject("The refresh token is no longer valid.");

            // Re-checked on refresh, not just at password grant. This is the only place an
            // account- or tenant-level suspension can be enforced for a client that already holds a
            // refresh token — no middleware runs inside the token exchange, so without this a
            // principal disabled after issuance keeps minting access tokens until the refresh
            // token's own lifetime runs out.
            if (await ValidateBeforeSignInAsync(user) is { } blocked)
                return blocked;

            return SignIn(await CreatePrincipalAsync(user, auth.Principal!.GetScopes()),
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        throw new InvalidOperationException("The specified grant type is not supported.");
    }

    /// <summary>
    /// Policy gate applied to a user whose credentials (or refresh token) already checked out, run on
    /// BOTH grants immediately before the principal is issued. Return null to allow sign-in, or an
    /// <see cref="IActionResult"/> — typically <see cref="Reject"/> — to refuse it.
    ///
    /// The framework has no opinion here; the default allows everything. It exists because a host's
    /// reasons for refusing an otherwise-valid credential (suspended account, deactivated tenant,
    /// expired subscription, forced password reset) are application policy, and the refresh_token
    /// grant is unreachable from middleware — so this is the only correct place to enforce them.
    /// </summary>
    protected virtual Task<IActionResult?> ValidateBeforeSignInAsync(SliceUser user) =>
        Task.FromResult<IActionResult?>(null);

    // Usernames are no longer globally unique once tenants exist, so a bare FindByNameAsync/
    // FindByEmailAsync can no longer safely resolve "the" user. Each tenant's account has its own
    // independent password hash, so the password itself is the natural disambiguator for the common
    // case: check every candidate row, not just the first match. An optional 'tenant_id' parameter
    // lets a caller that already knows which tenant it wants short-circuit straight to it.
    // Virtual so a host app can layer in its own tenant-scoping signals (e.g. an already-resolved
    // ICurrentTenant from Host-header/subdomain resolution) without duplicating this whole class.
    protected virtual async Task<LoginCandidates> ResolveUserAsync(string usernameOrEmail, string password, string? tenantIdHint)
    {
        var normalizedUserName = userManager.NormalizeName(usernameOrEmail);
        var normalizedEmail = userManager.NormalizeEmail(usernameOrEmail);
        var query = userManager.Users
            .Where(u => u.NormalizedUserName == normalizedUserName || u.NormalizedEmail == normalizedEmail);

        if (Guid.TryParse(tenantIdHint, out var tenantId))
            query = query.Where(u => u.TenantId == tenantId);

        // Bounded on purpose. Verifying a password is an intentionally expensive hash, and the loop
        // below runs one per candidate row — so an address shared by very many tenants would turn a
        // single unauthenticated request into that many hashes. Past the cap we make the caller
        // narrow it down with 'tenant_id' instead. Fetching cap+1 rows is what tells us we're over.
        var cap = Math.Max(1, Options.MaxLoginCandidates);
        var candidates = await query.OrderBy(u => u.Id).Take(cap + 1).ToListAsync();
        if (candidates.Count > cap)
            return LoginCandidates.TooMany;

        var matches = new List<SliceUser>();
        foreach (var candidate in candidates)
            if (await userManager.CheckPasswordAsync(candidate, password))
                matches.Add(candidate);

        return new LoginCandidates(matches);
    }

    // Virtual so a host app can enrich the ambiguous-match response with structured tenant choices
    // (e.g. tenant id + display name) instead of this generic message — lets a client render a
    // picker directly rather than needing the caller to already know which tenant_id to retry with.
    protected virtual Task<IActionResult> RejectAmbiguousAsync(IReadOnlyList<SliceUser> candidates) =>
        Task.FromResult<IActionResult>(Reject("Multiple accounts match these credentials; retry the request with a 'tenant_id' parameter."));

    /// <summary>
    /// Builds the principal that becomes the access token. Virtual so a host app can add its own
    /// claims; call the base first unless you mean to replace the tenant/role/permission set wholesale.
    /// </summary>
    protected virtual async Task<ClaimsPrincipal> CreatePrincipalAsync(SliceUser user, ImmutableArray<string> scopes)
    {
        var identity = new ClaimsIdentity(
            authenticationType: "OpenIddict",
            nameType: Claims.Name,
            roleType: Claims.Role);

        identity.SetClaim(Claims.Subject, user.Id.ToString())
                .SetClaim(Claims.Name, user.UserName)
                .SetClaim(Claims.Email, user.Email);

        if (user.TenantId is { } tenantId)
            identity.SetClaim(SliceClaims.TenantId, tenantId.ToString());

        // Resolved through the user's actual role links, not by re-looking-up role names. A name
        // re-lookup scoped to user.TenantId would silently drop any role deliberately granted from
        // outside the user's own tenant (a platform-tier support role on in-tenant staff, say): the
        // name would still land in the role claim while its permissions vanished.
        var roles = await tenantRoleAssigner.GetRolesOfUserAsync(user);
        identity.SetClaims(Claims.Role, [.. roles.Select(r => r.Name).OfType<string>()]);

        var permissions = new HashSet<string>(StringComparer.Ordinal);
        var denies = new HashSet<string>(StringComparer.Ordinal);
        var dataScopes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var role in roles)
        {
            foreach (var claim in await roleManager.GetClaimsAsync(role))
            {
                if (claim.Type == SliceClaims.Permission)
                    permissions.Add(claim.Value);
                else if (claim.Type == SliceClaims.PermissionDeny)
                    denies.Add(claim.Value);
                else if (claim.Type == SliceClaims.DataScope)
                    dataScopes.Add(claim.Value);
            }
        }

        // Denies are emitted alongside the grants rather than subtracted from them here, so that
        // ClaimsPermissionStore stays the single place the precedence rule lives.
        foreach (var permission in permissions)
            identity.AddClaim(new Claim(SliceClaims.Permission, permission));
        foreach (var deny in denies)
            identity.AddClaim(new Claim(SliceClaims.PermissionDeny, deny));
        foreach (var dataScope in dataScopes)
            identity.AddClaim(new Claim(SliceClaims.DataScope, dataScope));

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(scopes);
        identity.SetDestinations(_ => [Destinations.AccessToken]);
        return principal;
    }

}
