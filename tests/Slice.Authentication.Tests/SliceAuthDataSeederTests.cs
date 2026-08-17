using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Slice.Authorization;

namespace Slice.Authentication.Tests;

/// <summary>
/// The seeder runs on every application start, so it has to be idempotent. It also has to stay
/// idempotent now that it resolves the admin role/user by <c>(TenantId == null, Normalized*)</c>
/// instead of the stock by-name lookups: those queries use <c>SingleOrDefaultAsync</c>, so getting the
/// predicate wrong shows up as a second admin row on the first restart, or a throw on the next one.
/// </summary>
public sealed class SliceAuthDataSeederTests : AuthDbFixture
{
    private sealed class TestPermissions : PermissionDefinitionProvider
    {
        public override void Define(IPermissionDefinitionContext context)
        {
            var group = context.AddGroup("Test");
            group.AddPermission("test.read");
            group.AddPermission("test.write");
        }
    }

    [Fact]
    public async Task Seeding_twice_leaves_exactly_one_admin_role_and_user()
    {
        await Seed();
        await Seed();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SliceAuthDbContext>();
        var options = scope.ServiceProvider.GetRequiredService<SliceAuthOptions>();

        Assert.Equal(1, await db.Roles.CountAsync(r => r.TenantId == null));
        Assert.Equal(1, await db.Users.CountAsync(u => u.TenantId == null));

        var admin = await db.Users.SingleAsync();
        Assert.Equal(options.AdminEmail, admin.Email);
        Assert.Null(admin.TenantId);   // the demo admin is platform-tier, not scoped to any tenant
    }

    [Fact]
    public async Task Seeding_twice_does_not_duplicate_the_permission_role_claims()
    {
        await Seed();
        await Seed();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SliceAuthDbContext>();

        var permissionClaims = await db.RoleClaims
            .Where(c => c.ClaimType == SliceClaims.Permission)
            .Select(c => c.ClaimValue)
            .ToListAsync();

        Assert.Equal(["test.read", "test.write"], permissionClaims.Order());
    }

    [Fact]
    public async Task The_seeded_admin_is_assigned_the_admin_role()
    {
        await Seed();

        await using var scope = Services.CreateAsyncScope();
        var assigner = scope.ServiceProvider.GetRequiredService<ITenantRoleAssigner>();
        var db = scope.ServiceProvider.GetRequiredService<SliceAuthDbContext>();
        var options = scope.ServiceProvider.GetRequiredService<SliceAuthOptions>();

        var admin = await db.Users.SingleAsync();

        Assert.True(await assigner.IsInRoleAsync(admin, options.AdminRole));
    }

    private async Task Seed()
    {
        await using var scope = Services.CreateAsyncScope();
        await SliceAuthDataSeeder.SeedAsync(scope.ServiceProvider);
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<PermissionDefinitionProvider, TestPermissions>();
        services.AddSingleton<IPermissionDefinitionManager, PermissionDefinitionManager>();
    }
}
