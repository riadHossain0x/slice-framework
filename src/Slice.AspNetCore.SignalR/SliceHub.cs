using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace Slice.AspNetCore.SignalR;

/// <summary>
/// Base SignalR hub that surfaces the connected caller's identity and tenant from the connection
/// principal — the same <c>sub</c>/<c>tenant_id</c> claims the rest of Slice uses — so real-time
/// handlers are tenant- and user-aware without re-reading claims by hand.
/// </summary>
public abstract class SliceHub : Hub
{
    protected ClaimsPrincipal? CurrentPrincipal => Context.User;

    protected Guid? CurrentUserId => SliceHubClaims.UserId(Context.User);
    protected Guid? CurrentTenantId => SliceHubClaims.TenantId(Context.User);
    protected string? CurrentUserName => Context.User?.Identity?.Name;
}

/// <summary>Strongly-typed variant (typed client proxy) with the same identity helpers.</summary>
public abstract class SliceHub<TClient> : Hub<TClient> where TClient : class
{
    protected ClaimsPrincipal? CurrentPrincipal => Context.User;

    protected Guid? CurrentUserId => SliceHubClaims.UserId(Context.User);
    protected Guid? CurrentTenantId => SliceHubClaims.TenantId(Context.User);
    protected string? CurrentUserName => Context.User?.Identity?.Name;
}

internal static class SliceHubClaims
{
    public static Guid? UserId(ClaimsPrincipal? user)
    {
        var value = user?.FindFirst("sub")?.Value ?? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(value, out var id) ? id : null;
    }

    public static Guid? TenantId(ClaimsPrincipal? user)
        => Guid.TryParse(user?.FindFirst("tenant_id")?.Value, out var id) ? id : null;
}
