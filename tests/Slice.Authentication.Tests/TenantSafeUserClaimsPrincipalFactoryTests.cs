using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Slice.Authentication.Tests;

/// <summary>
/// Guards the reason <see cref="TenantSafeUserClaimsPrincipalFactory"/> exists. The stock role-aware
/// factory expands role NAMES back into roles through an unfiltered <c>FindByNameAsync</c>, so once
/// two tenants each have a "Manager" it can merge the wrong tenant's claims into a signed-in
/// principal. If someone ever "simplifies" this class back to the default, these fail.
/// </summary>
public sealed class TenantSafeUserClaimsPrincipalFactoryTests : AuthDbFixture
{
    [Fact]
    public async Task Principal_gets_only_its_own_tenants_claims_when_role_names_collide()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using var scope = Services.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<SliceRole>>();

        var roleA = new SliceRole { Name = "Manager", TenantId = tenantA };
        var roleB = new SliceRole { Name = "Manager", TenantId = tenantB };
        await roles.CreateAsync(roleA);
        await roles.CreateAsync(roleB);
        await roles.AddClaimAsync(roleA, new Claim(SliceClaims.Permission, "tenant-a.only"));
        await roles.AddClaimAsync(roleB, new Claim(SliceClaims.Permission, "tenant-b.only"));

        var user = new SliceUser { UserName = "staff@test.local", Email = "staff@test.local", TenantId = tenantA };
        await scope.ServiceProvider.GetRequiredService<UserManager<SliceUser>>().CreateAsync(user, "Password1!");
        await scope.ServiceProvider.GetRequiredService<ITenantRoleAssigner>().AddToRoleAsync(user, "Manager");

        var principal = await Factory(scope.ServiceProvider).CreateAsync(user);
        var permissions = principal.FindAll(SliceClaims.Permission).Select(c => c.Value).ToList();

        Assert.Equal(["tenant-a.only"], permissions);
        Assert.DoesNotContain("tenant-b.only", permissions);
    }

    [Fact]
    public async Task Principal_carries_the_tenant_id_and_role_name_claims()
    {
        var tenant = Guid.NewGuid();

        await using var scope = Services.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<SliceRole>>();
        await roles.CreateAsync(new SliceRole { Name = "Manager", TenantId = tenant });

        var user = new SliceUser { UserName = "staff@test.local", Email = "staff@test.local", TenantId = tenant };
        await scope.ServiceProvider.GetRequiredService<UserManager<SliceUser>>().CreateAsync(user, "Password1!");
        await scope.ServiceProvider.GetRequiredService<ITenantRoleAssigner>().AddToRoleAsync(user, "Manager");

        var principal = await Factory(scope.ServiceProvider).CreateAsync(user);

        Assert.Equal(tenant.ToString(), principal.FindFirst(SliceClaims.TenantId)?.Value);
        Assert.True(principal.IsInRole("Manager"));
    }

    [Fact]
    public async Task A_platform_user_gets_no_tenant_id_claim()
    {
        await using var scope = Services.CreateAsyncScope();
        var user = new SliceUser { UserName = "root@test.local", Email = "root@test.local", TenantId = null };
        await scope.ServiceProvider.GetRequiredService<UserManager<SliceUser>>().CreateAsync(user, "Password1!");

        var principal = await Factory(scope.ServiceProvider).CreateAsync(user);

        Assert.Null(principal.FindFirst(SliceClaims.TenantId));
    }

    private static IUserClaimsPrincipalFactory<SliceUser> Factory(IServiceProvider sp)
        => sp.GetRequiredService<IUserClaimsPrincipalFactory<SliceUser>>();
}
