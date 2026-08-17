using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Slice.Authentication.Tests;

public sealed class TenantScopedValidatorTests : AuthDbFixture
{
    [Fact]
    public async Task Same_username_across_two_tenants_validates_successfully()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using var scope = Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SliceUser>>();

        var resultA = await userManager.CreateAsync(
            new SliceUser { UserName = "shared@test.local", Email = "shared@test.local", TenantId = tenantA }, "Password1!");
        var resultB = await userManager.CreateAsync(
            new SliceUser { UserName = "shared@test.local", Email = "shared@test.local", TenantId = tenantB }, "Password1!");

        Assert.True(resultA.Succeeded);
        Assert.True(resultB.Succeeded);
    }

    [Fact]
    public async Task Duplicate_username_within_the_same_tenant_fails_validation()
    {
        var tenant = Guid.NewGuid();

        await using var scope = Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SliceUser>>();

        var first = await userManager.CreateAsync(
            new SliceUser { UserName = "dup@test.local", Email = "dup@test.local", TenantId = tenant }, "Password1!");
        Assert.True(first.Succeeded);

        var second = await userManager.CreateAsync(
            new SliceUser { UserName = "dup@test.local", Email = "dup@test.local", TenantId = tenant }, "Password1!");

        Assert.False(second.Succeeded);
        Assert.Contains(second.Errors, e => e.Code == "DuplicateUserName");
    }

    [Fact]
    public async Task Same_role_name_across_two_tenants_validates_successfully()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using var scope = Services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<SliceRole>>();

        var resultA = await roleManager.CreateAsync(new SliceRole { Name = "Manager", TenantId = tenantA });
        var resultB = await roleManager.CreateAsync(new SliceRole { Name = "Manager", TenantId = tenantB });

        Assert.True(resultA.Succeeded);
        Assert.True(resultB.Succeeded);
    }

    [Fact]
    public async Task Duplicate_role_name_within_the_same_tenant_fails_validation()
    {
        var tenant = Guid.NewGuid();

        await using var scope = Services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<SliceRole>>();

        var first = await roleManager.CreateAsync(new SliceRole { Name = "Manager", TenantId = tenant });
        Assert.True(first.Succeeded);

        var second = await roleManager.CreateAsync(new SliceRole { Name = "Manager", TenantId = tenant });

        Assert.False(second.Succeeded);
        Assert.Contains(second.Errors, e => e.Code == "DuplicateRoleName");
    }

    // Identity's own RequireUniqueEmail is switched off because it can only check globally. These two
    // pin down what replaced it — without them, dropping that switch would leave email uniqueness
    // enforced nowhere, contradicting the "once per tenant" guarantee the docs give.

    [Fact]
    public async Task Duplicate_email_within_the_same_tenant_fails_validation()
    {
        var tenant = Guid.NewGuid();

        await using var scope = Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SliceUser>>();

        var first = await userManager.CreateAsync(
            new SliceUser { UserName = "first", Email = "shared@test.local", TenantId = tenant }, "Password1!");
        Assert.True(first.Succeeded);

        // Different username, same email, same tenant — the username check alone would let this pass.
        var second = await userManager.CreateAsync(
            new SliceUser { UserName = "second", Email = "shared@test.local", TenantId = tenant }, "Password1!");

        Assert.False(second.Succeeded);
        Assert.Contains(second.Errors, e => e.Code == "DuplicateEmail");
    }

    [Fact]
    public async Task Same_email_across_two_tenants_validates_successfully()
    {
        await using var scope = Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SliceUser>>();

        var resultA = await userManager.CreateAsync(
            new SliceUser { UserName = "acme-admin", Email = "admin@test.local", TenantId = Guid.NewGuid() }, "Password1!");
        var resultB = await userManager.CreateAsync(
            new SliceUser { UserName = "globex-admin", Email = "admin@test.local", TenantId = Guid.NewGuid() }, "Password1!");

        Assert.True(resultA.Succeeded);
        Assert.True(resultB.Succeeded);
    }

    [Fact]
    public async Task Duplicate_email_across_two_platform_tier_accounts_fails_validation()
    {
        await using var scope = Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SliceUser>>();

        var first = await userManager.CreateAsync(
            new SliceUser { UserName = "root", Email = "ops@test.local", TenantId = null }, "Password1!");
        Assert.True(first.Succeeded);

        var second = await userManager.CreateAsync(
            new SliceUser { UserName = "root2", Email = "ops@test.local", TenantId = null }, "Password1!");

        Assert.False(second.Succeeded);
        Assert.Contains(second.Errors, e => e.Code == "DuplicateEmail");
    }

    // The module registers this validator via RemoveAll<IUserValidator<SliceUser>>(), so anything the
    // stock UserValidator checked and this one doesn't is enforced NOWHERE. These cover the rules that
    // have nothing to do with tenancy and must survive the swap.

    [Theory]
    [InlineData("has space")]
    [InlineData("has\nnewline")]
    [InlineData("sla/sh")]
    [InlineData("<script>")]
    public async Task Usernames_outside_the_allowed_character_set_are_rejected(string userName)
    {
        await using var scope = Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SliceUser>>();

        var result = await userManager.CreateAsync(
            new SliceUser { UserName = userName, TenantId = Guid.NewGuid() }, "Password1!");

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Code == "InvalidUserName");
    }

    [Fact]
    public async Task A_username_from_the_allowed_character_set_is_accepted()
    {
        await using var scope = Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SliceUser>>();

        var result = await userManager.CreateAsync(
            new SliceUser { UserName = "a.b-c_d+e@f", TenantId = Guid.NewGuid() }, "Password1!");

        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    [Fact]
    public async Task A_malformed_email_is_rejected()
    {
        await using var scope = Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SliceUser>>();

        var result = await userManager.CreateAsync(
            new SliceUser { UserName = "someone", Email = "not-an-email", TenantId = Guid.NewGuid() }, "Password1!");

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Code == "InvalidEmail");
    }

    [Fact]
    public async Task Users_without_an_email_do_not_collide_with_each_other()
    {
        var tenant = Guid.NewGuid();

        await using var scope = Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SliceUser>>();

        // Email is optional in Identity. The uniqueness rule must not turn "no email" into a value
        // that two service accounts in one tenant can conflict on.
        var first = await userManager.CreateAsync(new SliceUser { UserName = "svc-a", TenantId = tenant }, "Password1!");
        var second = await userManager.CreateAsync(new SliceUser { UserName = "svc-b", TenantId = tenant }, "Password1!");

        Assert.True(first.Succeeded, string.Join("; ", first.Errors.Select(e => e.Description)));
        Assert.True(second.Succeeded, string.Join("; ", second.Errors.Select(e => e.Description)));
    }
}
