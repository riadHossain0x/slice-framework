using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Slice.Authentication.Tests;

public sealed class TenantRoleAssignerTests : AuthDbFixture
{
    [Fact]
    public async Task FindRoleAsync_resolves_the_correct_tenants_role_when_names_collide()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using var scope = Services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<SliceRole>>();
        var assigner = scope.ServiceProvider.GetRequiredService<ITenantRoleAssigner>();

        var roleA = new SliceRole { Name = "Manager", TenantId = tenantA };
        var roleB = new SliceRole { Name = "Manager", TenantId = tenantB };
        await roleManager.CreateAsync(roleA);
        await roleManager.CreateAsync(roleB);

        var resolvedA = await assigner.FindRoleAsync("Manager", tenantA);
        var resolvedB = await assigner.FindRoleAsync("Manager", tenantB);

        Assert.Equal(roleA.Id, resolvedA!.Id);
        Assert.Equal(roleB.Id, resolvedB!.Id);
    }

    [Fact]
    public async Task FindRoleAsync_returns_null_for_a_null_name_rather_than_throwing()
    {
        await using var scope = Services.CreateAsyncScope();
        var assigner = scope.ServiceProvider.GetRequiredService<ITenantRoleAssigner>();

        // KeyNormalizer.NormalizeName(null) returns null; the query must not be run with it.
        Assert.Null(await assigner.FindRoleAsync(null!, Guid.NewGuid()));
        Assert.Null(await assigner.FindRoleAsync(null!, null));
    }

    [Fact]
    public async Task A_null_name_in_a_batch_add_reports_RoleNotFound_rather_than_throwing()
    {
        var tenant = Guid.NewGuid();

        await using var scope = Services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<SliceRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SliceUser>>();
        var assigner = scope.ServiceProvider.GetRequiredService<ITenantRoleAssigner>();

        await roleManager.CreateAsync(new SliceRole { Name = "Manager", TenantId = tenant });
        var user = new SliceUser { UserName = "staff@test.local", Email = "staff@test.local", TenantId = tenant };
        await userManager.CreateAsync(user, "Password1!");

        // A null name is a caller error, but it must surface as "that role does not exist" rather than
        // as a fault from inside the name-resolution plumbing.
        var result = await assigner.AddToRolesAsync(user, ["Manager", null!]);

        Assert.False(result.Succeeded);
        var error = Assert.Single(result.Errors, e => e.Code == "RoleNotFound");
        Assert.Contains("(null)", error.Description);

        // All-or-nothing still holds: the valid name in the batch was not assigned either.
        Assert.False(await assigner.IsInRoleAsync(user, "Manager"));
    }

    [Fact]
    public async Task A_null_name_in_a_batch_remove_reports_RoleNotFound_rather_than_throwing()
    {
        var tenant = Guid.NewGuid();

        await using var scope = Services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<SliceRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SliceUser>>();
        var assigner = scope.ServiceProvider.GetRequiredService<ITenantRoleAssigner>();

        await roleManager.CreateAsync(new SliceRole { Name = "Manager", TenantId = tenant });
        var user = new SliceUser { UserName = "staff@test.local", Email = "staff@test.local", TenantId = tenant };
        await userManager.CreateAsync(user, "Password1!");
        Assert.True((await assigner.AddToRoleAsync(user, "Manager")).Succeeded);

        var result = await assigner.RemoveFromRolesAsync(user, [null!]);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Code == "RoleNotFound");

        // The removal was rejected, not half-applied.
        Assert.True(await assigner.IsInRoleAsync(user, "Manager"));
    }

    [Fact]
    public async Task AddToRoleAsync_only_assigns_within_the_users_own_tenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using var scope = Services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<SliceRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SliceUser>>();
        var assigner = scope.ServiceProvider.GetRequiredService<ITenantRoleAssigner>();

        await roleManager.CreateAsync(new SliceRole { Name = "Manager", TenantId = tenantA });
        var roleB = new SliceRole { Name = "Manager", TenantId = tenantB };
        await roleManager.CreateAsync(roleB);

        var user = new SliceUser { UserName = "staff@test.local", Email = "staff@test.local", TenantId = tenantA };
        var created = await userManager.CreateAsync(user, "Password1!");
        Assert.True(created.Succeeded);

        var result = await assigner.AddToRoleAsync(user, "Manager");

        Assert.True(result.Succeeded);
        Assert.Equal(0, await assigner.CountUsersInRoleAsync(roleB)); // tenant B's identically-named role is untouched
    }

    [Fact]
    public async Task AddToRoleAsync_fails_when_the_role_does_not_exist_for_the_users_tenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using var scope = Services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<SliceRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SliceUser>>();
        var assigner = scope.ServiceProvider.GetRequiredService<ITenantRoleAssigner>();

        // "Manager" only exists for tenant B, not tenant A.
        await roleManager.CreateAsync(new SliceRole { Name = "Manager", TenantId = tenantB });

        var user = new SliceUser { UserName = "staff@test.local", Email = "staff@test.local", TenantId = tenantA };
        await userManager.CreateAsync(user, "Password1!");

        var result = await assigner.AddToRoleAsync(user, "Manager");

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Code == "RoleNotFound");
    }

    [Fact]
    public async Task RemoveFromRolesAsync_removes_only_the_intended_assignment()
    {
        var tenant = Guid.NewGuid();

        await using var scope = Services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<SliceRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SliceUser>>();
        var assigner = scope.ServiceProvider.GetRequiredService<ITenantRoleAssigner>();

        var role = new SliceRole { Name = "Manager", TenantId = tenant };
        await roleManager.CreateAsync(role);
        var user = new SliceUser { UserName = "staff@test.local", Email = "staff@test.local", TenantId = tenant };
        await userManager.CreateAsync(user, "Password1!");
        await assigner.AddToRoleAsync(user, "Manager");

        Assert.Equal(1, await assigner.CountUsersInRoleAsync(role));

        await assigner.RemoveFromRolesAsync(user, ["Manager"]);

        Assert.Equal(0, await assigner.CountUsersInRoleAsync(role));
    }

    [Fact]
    public async Task Role_names_resolve_case_insensitively_like_the_stock_APIs()
    {
        var tenant = Guid.NewGuid();

        await using var scope = Services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<SliceRole>>();
        var assigner = scope.ServiceProvider.GetRequiredService<ITenantRoleAssigner>();

        await roleManager.CreateAsync(new SliceRole { Name = "Manager", TenantId = tenant });

        // FindRolesAsync must agree with FindRoleAsync about what "manager" means. Matching the raw
        // Name column instead of NormalizedName made the two disagree, so a lowercase name resolved
        // through one and silently returned nothing through the other.
        Assert.NotNull(await assigner.FindRoleAsync("manager", tenant));
        Assert.Single(await assigner.FindRolesAsync(["manager"], tenant));
        Assert.Single(await assigner.FindRolesAsync(["MANAGER"], tenant));
    }

    [Fact]
    public async Task RemoveFromRolesAsync_reports_a_role_that_does_not_exist_for_the_tenant()
    {
        var tenant = Guid.NewGuid();

        await using var scope = Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SliceUser>>();
        var assigner = scope.ServiceProvider.GetRequiredService<ITenantRoleAssigner>();

        var user = new SliceUser { UserName = "staff@test.local", Email = "staff@test.local", TenantId = tenant };
        await userManager.CreateAsync(user, "Password1!");

        // Reporting success for a removal that removed nothing reads as "the user no longer has that
        // role", which would be a false assurance to any caller checking the result.
        var result = await assigner.RemoveFromRolesAsync(user, ["NoSuchRole"]);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Code == "RoleNotFound");
    }

    [Fact]
    public async Task AddToRolesAsync_assigns_nothing_when_any_name_in_the_batch_is_unknown()
    {
        var tenant = Guid.NewGuid();

        await using var scope = Services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<SliceRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SliceUser>>();
        var assigner = scope.ServiceProvider.GetRequiredService<ITenantRoleAssigner>();

        var known = new SliceRole { Name = "Manager", TenantId = tenant };
        await roleManager.CreateAsync(known);
        var user = new SliceUser { UserName = "staff@test.local", Email = "staff@test.local", TenantId = tenant };
        await userManager.CreateAsync(user, "Password1!");

        var result = await assigner.AddToRolesAsync(user, ["Manager", "NoSuchRole"]);

        Assert.False(result.Succeeded);
        Assert.Equal(0, await assigner.CountUsersInRoleAsync(known));   // not a partial assignment
    }

    [Fact]
    public async Task GetRolesOfUserAsync_returns_a_role_granted_from_outside_the_users_tenant()
    {
        var tenant = Guid.NewGuid();

        await using var scope = Services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<SliceRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SliceUser>>();
        var assigner = scope.ServiceProvider.GetRequiredService<ITenantRoleAssigner>();
        var db = scope.ServiceProvider.GetRequiredService<SliceAuthDbContext>();

        var platformRole = new SliceRole { Name = "Support", TenantId = null };
        await roleManager.CreateAsync(platformRole);
        var user = new SliceUser { UserName = "staff@test.local", Email = "staff@test.local", TenantId = tenant };
        await userManager.CreateAsync(user, "Password1!");

        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = platformRole.Id });
        await db.SaveChangesAsync();

        // Resolving by (name, user.TenantId) would drop this; resolving by link keeps it.
        var roles = await assigner.GetRolesOfUserAsync(user);

        Assert.Equal(platformRole.Id, Assert.Single(roles).Id);
        Assert.Empty(await assigner.FindRolesAsync(["Support"], user.TenantId));
    }

    [Fact]
    public async Task IsInRoleAsync_is_scoped_to_the_users_own_tenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using var scope = Services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<SliceRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SliceUser>>();
        var assigner = scope.ServiceProvider.GetRequiredService<ITenantRoleAssigner>();

        await roleManager.CreateAsync(new SliceRole { Name = "Manager", TenantId = tenantA });
        await roleManager.CreateAsync(new SliceRole { Name = "Manager", TenantId = tenantB });

        var userA = new SliceUser { UserName = "a@test.local", Email = "a@test.local", TenantId = tenantA };
        var userB = new SliceUser { UserName = "b@test.local", Email = "b@test.local", TenantId = tenantB };
        await userManager.CreateAsync(userA, "Password1!");
        await userManager.CreateAsync(userB, "Password1!");
        await assigner.AddToRoleAsync(userA, "Manager");

        Assert.True(await assigner.IsInRoleAsync(userA, "Manager"));
        Assert.False(await assigner.IsInRoleAsync(userB, "Manager"));   // B's own "Manager", not A's
    }

    [Fact]
    public async Task IsInRoleAsync_agrees_with_GetRolesOfUserAsync_about_a_cross_tenant_grant()
    {
        var tenant = Guid.NewGuid();

        await using var scope = Services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<SliceRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SliceUser>>();
        var assigner = scope.ServiceProvider.GetRequiredService<ITenantRoleAssigner>();
        var db = scope.ServiceProvider.GetRequiredService<SliceAuthDbContext>();

        var platformRole = new SliceRole { Name = "Support", TenantId = null };
        await roleManager.CreateAsync(platformRole);
        var user = new SliceUser { UserName = "staff@test.local", Email = "staff@test.local", TenantId = tenant };
        await userManager.CreateAsync(user, "Password1!");
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = platformRole.Id });
        await db.SaveChangesAsync();

        // This role is on the user's token and [Authorize(Roles = "Support")] honors it, so the
        // interface's own "is this user in that role" answer must not say otherwise.
        Assert.Contains(await assigner.GetRolesOfUserAsync(user), r => r.Name == "Support");
        Assert.True(await assigner.IsInRoleAsync(user, "Support"));
    }

    [Fact]
    public async Task IsInRoleAsync_is_case_insensitive()
    {
        var tenant = Guid.NewGuid();

        await using var scope = Services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<SliceRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SliceUser>>();
        var assigner = scope.ServiceProvider.GetRequiredService<ITenantRoleAssigner>();

        await roleManager.CreateAsync(new SliceRole { Name = "Manager", TenantId = tenant });
        var user = new SliceUser { UserName = "staff@test.local", Email = "staff@test.local", TenantId = tenant };
        await userManager.CreateAsync(user, "Password1!");
        await assigner.AddToRoleAsync(user, "Manager");

        Assert.True(await assigner.IsInRoleAsync(user, "manager"));
    }

    [Fact]
    public async Task GetUsersInRoleAsync_returns_the_members_of_that_exact_role()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using var scope = Services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<SliceRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SliceUser>>();
        var assigner = scope.ServiceProvider.GetRequiredService<ITenantRoleAssigner>();

        var roleA = new SliceRole { Name = "Manager", TenantId = tenantA };
        var roleB = new SliceRole { Name = "Manager", TenantId = tenantB };
        await roleManager.CreateAsync(roleA);
        await roleManager.CreateAsync(roleB);

        var userA = new SliceUser { UserName = "a@test.local", Email = "a@test.local", TenantId = tenantA };
        var userB = new SliceUser { UserName = "b@test.local", Email = "b@test.local", TenantId = tenantB };
        await userManager.CreateAsync(userA, "Password1!");
        await userManager.CreateAsync(userB, "Password1!");
        await assigner.AddToRoleAsync(userA, "Manager");
        await assigner.AddToRoleAsync(userB, "Manager");

        Assert.Equal(userA.Id, Assert.Single(await assigner.GetUsersInRoleAsync(roleA)).Id);
        Assert.Equal(userB.Id, Assert.Single(await assigner.GetUsersInRoleAsync(roleB)).Id);
    }
}
