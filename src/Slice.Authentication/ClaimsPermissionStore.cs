using Microsoft.AspNetCore.Http;
using Slice.Authorization;
using Slice.Core.DependencyInjection;

namespace Slice.Authentication;

/// <summary>Permission/tenant claim types embedded in issued tokens.</summary>
public static class SliceClaims
{
    public const string Permission = "permission";
    public const string PermissionDeny = "permission_deny";
    public const string DataScope = "data_scope";

    /// <summary>Same claim type Slice.MultiTenancy's ClaimTenantResolveContributor already reads.</summary>
    public const string TenantId = Slice.Domain.MultiTenancy.MultiTenancyClaims.TenantId;
}

/// <summary>
/// Default <see cref="IPermissionStore"/> once authentication is present: a permission is granted
/// when the current principal carries a matching <c>permission</c> claim (placed in the token at
/// issuance from the user's roles) and does NOT carry a matching <c>permission_deny</c> claim.
/// Registered after the P7 config store, so it wins.
/// </summary>
public sealed class ClaimsPermissionStore(IHttpContextAccessor httpContextAccessor)
    : IPermissionStore, ISingletonDependency
{
    public Task<bool> IsGrantedAsync(string permission, CancellationToken ct = default)
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user == null) return Task.FromResult(false);

        if (user.HasClaim(SliceClaims.PermissionDeny, permission))
            return Task.FromResult(false);

        var granted = user.HasClaim(SliceClaims.Permission, permission);
        return Task.FromResult(granted);
    }
}
