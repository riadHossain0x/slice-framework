using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Slice.AspNetCore.Mvc;
using Slice.Authentication;
using Slice.Core.Ambient;

namespace Slice.Management;

public sealed record GrantPermissionRequest(string ProviderName, string ProviderKey, string Permission);

[Authorize]
[Route("api/management/permissions")]
public sealed class PermissionManagementController(IPermissionGrantManager grants) : SliceController
{
    [HttpGet]
    public async Task<IActionResult> Get(string providerName, string providerKey, CancellationToken ct)
        => Ok(await grants.GetGrantedAsync(providerName, providerKey, ct));

    [HttpPost("grant")]
    public async Task<IActionResult> Grant([FromBody] GrantPermissionRequest r, CancellationToken ct)
    {
        await grants.GrantAsync(r.ProviderName, r.ProviderKey, r.Permission, ct);
        return Ok();
    }

    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke([FromBody] GrantPermissionRequest r, CancellationToken ct)
    {
        await grants.RevokeAsync(r.ProviderName, r.ProviderKey, r.Permission, ct);
        return Ok();
    }
}

public sealed record CreateTenantRequest(string Name, string? ConnectionString = null);

[Authorize]
[Route("api/management/tenants")]
public sealed class TenantManagementController(ITenantManager tenants) : SliceController
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await tenants.GetListAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTenantRequest r, CancellationToken ct)
        => Ok(await tenants.CreateAsync(r.Name, r.ConnectionString, ct));
}

public sealed record SetValueRequest(string Name, string? Value, string ProviderName, string? ProviderKey = null);

[Authorize]
[Route("api/management/features")]
public sealed class FeatureManagementController(IFeatureValueManager features) : SliceController
{
    [HttpGet]
    public async Task<IActionResult> Get(string name, string providerName, string? providerKey, CancellationToken ct)
        => Ok(await features.GetAsync(name, providerName, providerKey, ct));

    [HttpPut]
    public async Task<IActionResult> Set([FromBody] SetValueRequest r, CancellationToken ct)
    {
        await features.SetAsync(r.Name, r.Value, r.ProviderName, r.ProviderKey, ct);
        return Ok();
    }

    [HttpDelete]
    public async Task<IActionResult> Clear(string name, string providerName, string? providerKey, CancellationToken ct)
    {
        await features.ClearAsync(name, providerName, providerKey, ct);
        return Ok();
    }
}

[Authorize]
[Route("api/management/settings")]
public sealed class SettingManagementController(ISettingValueManager settings) : SliceController
{
    [HttpGet]
    public async Task<IActionResult> Get(string name, string providerName, string? providerKey, CancellationToken ct)
        => Ok(await settings.GetAsync(name, providerName, providerKey, ct));

    [HttpPut]
    public async Task<IActionResult> Set([FromBody] SetValueRequest r, CancellationToken ct)
    {
        await settings.SetAsync(r.Name, r.Value, r.ProviderName, r.ProviderKey, ct);
        return Ok();
    }

    [HttpDelete]
    public async Task<IActionResult> Clear(string name, string providerName, string? providerKey, CancellationToken ct)
    {
        await settings.ClearAsync(name, providerName, providerKey, ct);
        return Ok();
    }
}

/// <param name="TenantId">
/// Which tenant to create the account in. Ignored unless the caller is a platform-tier operator
/// (no ambient tenant); an in-tenant caller always provisions into their own tenant, never another.
/// Null on a platform-tier call creates a platform-tier account.
/// </param>
public sealed record CreateUserRequest(string Email, string Password, string? Role, Guid? TenantId = null);

/// <param name="TenantId">As <see cref="CreateUserRequest.TenantId"/>.</param>
public sealed record CreateRoleRequest(string Name, Guid? TenantId = null);

/// <summary>
/// Identity administration. Every account and role created here is stamped with a tenant, because
/// <see cref="SliceUser"/>/<see cref="SliceRole"/> are tenant-scoped: leaving <c>TenantId</c> unset
/// would silently create platform-tier — i.e. cross-tenant — principals. Role lookups go through
/// <see cref="ITenantRoleAssigner"/> rather than the stock by-name Identity APIs, which resolve a
/// bare <c>NormalizedName</c> with no tenant filter and so can hit another tenant's identically
/// named role. See docs/multitenancy.md ("Tenant-scoped identity").
/// </summary>
[Authorize]
[Route("api/management/identity")]
public sealed class IdentityManagementController(
    UserManager<SliceUser> users,
    RoleManager<SliceRole> roles,
    ITenantRoleAssigner roleAssigner,
    ICurrentTenant currentTenant) : SliceController
{
    [HttpPost("roles")]
    public async Task<IActionResult> CreateRole([FromBody] CreateRoleRequest r, CancellationToken ct)
    {
        var tenantId = ResolveTargetTenant(r.TenantId);

        if (await roleAssigner.FindRoleAsync(r.Name, tenantId, ct) is not null) return Conflict();

        var result = await roles.CreateAsync(new SliceRole { Name = r.Name, TenantId = tenantId });
        return result.Succeeded
            ? Ok()
            : BadRequest(result.Errors.Select(e => e.Description));
    }

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest r, CancellationToken ct)
    {
        var tenantId = ResolveTargetTenant(r.TenantId);

        var user = new SliceUser { UserName = r.Email, Email = r.Email, EmailConfirmed = true, TenantId = tenantId };
        var result = await users.CreateAsync(user, r.Password);
        if (!result.Succeeded)
            return BadRequest(result.Errors.Select(e => e.Description));

        if (!string.IsNullOrWhiteSpace(r.Role))
        {
            var assigned = await roleAssigner.AddToRoleAsync(user, r.Role, ct);
            if (!assigned.Succeeded)
                return BadRequest(assigned.Errors.Select(e => e.Description));
        }

        return Ok(new { user.Id, user.Email, user.TenantId });
    }

    /// <summary>
    /// The tenant a newly created principal belongs to. An in-tenant caller is pinned to their own
    /// tenant — honoring a client-supplied id there would let any tenant admin mint principals inside
    /// another tenant, or platform-tier ones. Only a platform-tier caller may target a tenant
    /// explicitly, or fall back to whichever tenant the request is ambiently operating on.
    ///
    /// The caller's own tenant is read from their <c>tenant_id</c> token claim rather than from
    /// <see cref="ICurrentTenant"/>. Both usually agree, but the ambient tenant can also come from the
    /// <c>X-Tenant-Id</c> header (see <c>HeaderTenantResolveContributor</c>) — which the caller
    /// controls — so it is a routing signal, not an authorization boundary. The signed claim is.
    /// </summary>
    private Guid? ResolveTargetTenant(Guid? requested)
        => CallersOwnTenant() ?? requested ?? currentTenant.Id;

    private Guid? CallersOwnTenant()
        => Guid.TryParse(User.FindFirst(SliceClaims.TenantId)?.Value, out var tenantId) ? tenantId : null;
}
