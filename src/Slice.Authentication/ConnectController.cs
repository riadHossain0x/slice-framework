using System.Collections.Immutable;
using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Slice.Authentication;

/// <summary>OAuth2/OIDC token endpoint. Handles the password and refresh_token grants.</summary>
public sealed class ConnectController(UserManager<SliceUser> userManager, RoleManager<SliceRole> roleManager)
    : ControllerBase
{
    [HttpPost("~/connect/token"), Produces("application/json")]
    public async Task<IActionResult> Exchange()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenIddict request cannot be retrieved.");

        if (request.IsPasswordGrantType())
        {
            var user = await userManager.FindByNameAsync(request.Username!)
                       ?? await userManager.FindByEmailAsync(request.Username!);

            if (user is null || !await userManager.CheckPasswordAsync(user, request.Password!))
                return Reject("The username/password couple is invalid.");

            return SignIn(await CreatePrincipalAsync(user, request.GetScopes()),
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (request.IsRefreshTokenGrantType())
        {
            var auth = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            var subject = auth.Principal?.GetClaim(Claims.Subject);
            var user = subject is null ? null : await userManager.FindByIdAsync(subject);
            if (user is null)
                return Reject("The refresh token is no longer valid.");

            return SignIn(await CreatePrincipalAsync(user, auth.Principal!.GetScopes()),
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        throw new InvalidOperationException("The specified grant type is not supported.");
    }

    private async Task<ClaimsPrincipal> CreatePrincipalAsync(SliceUser user, ImmutableArray<string> scopes)
    {
        var identity = new ClaimsIdentity(
            authenticationType: "OpenIddict",
            nameType: Claims.Name,
            roleType: Claims.Role);

        identity.SetClaim(Claims.Subject, user.Id.ToString())
                .SetClaim(Claims.Name, user.UserName)
                .SetClaim(Claims.Email, user.Email);

        var roles = await userManager.GetRolesAsync(user);
        identity.SetClaims(Claims.Role, [.. roles]);

        var permissions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var roleName in roles)
        {
            var role = await roleManager.FindByNameAsync(roleName);
            if (role is null) continue;
            foreach (var claim in await roleManager.GetClaimsAsync(role))
                if (claim.Type == SliceClaims.Permission)
                    permissions.Add(claim.Value);
        }
        foreach (var permission in permissions)
            identity.AddClaim(new Claim(SliceClaims.Permission, permission));

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(scopes);
        identity.SetDestinations(_ => [Destinations.AccessToken]);
        return principal;
    }

    private ForbidResult Reject(string description) => Forbid(
        authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
        properties: new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
        }));
}
