using Microsoft.AspNetCore.Http;
using Slice.Authorization;
using Slice.Core.DependencyInjection;

namespace Slice.Authentication;

/// <summary>Permission claim type embedded in issued tokens.</summary>
public static class SliceClaims
{
    public const string Permission = "permission";
}

/// <summary>
/// Default <see cref="IPermissionStore"/> once authentication is present: a permission is granted
/// when the current principal carries a matching <c>permission</c> claim (placed in the token at
/// issuance from the user's roles). Registered after the P7 config store, so it wins.
/// </summary>
public sealed class ClaimsPermissionStore(IHttpContextAccessor httpContextAccessor)
    : IPermissionStore, ISingletonDependency
{
    public Task<bool> IsGrantedAsync(string permission, CancellationToken ct = default)
    {
        var user = httpContextAccessor.HttpContext?.User;
        var granted = user?.HasClaim(SliceClaims.Permission, permission) ?? false;
        return Task.FromResult(granted);
    }
}
