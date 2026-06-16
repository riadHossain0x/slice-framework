using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Slice.AspNetCore.Mvc;
using Slice.Authentication;

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

public sealed record CreateUserRequest(string Email, string Password, string? Role);
public sealed record CreateRoleRequest(string Name);

[Authorize]
[Route("api/management/identity")]
public sealed class IdentityManagementController(UserManager<SliceUser> users, RoleManager<SliceRole> roles) : SliceController
{
    [HttpPost("roles")]
    public async Task<IActionResult> CreateRole([FromBody] CreateRoleRequest r, CancellationToken ct)
    {
        if (await roles.RoleExistsAsync(r.Name)) return Conflict();
        await roles.CreateAsync(new SliceRole { Name = r.Name });
        return Ok();
    }

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest r, CancellationToken ct)
    {
        var user = new SliceUser { UserName = r.Email, Email = r.Email, EmailConfirmed = true };
        var result = await users.CreateAsync(user, r.Password);
        if (!result.Succeeded)
            return BadRequest(result.Errors.Select(e => e.Description));
        if (!string.IsNullOrWhiteSpace(r.Role))
            await users.AddToRoleAsync(user, r.Role);
        return Ok(new { user.Id, user.Email });
    }
}
