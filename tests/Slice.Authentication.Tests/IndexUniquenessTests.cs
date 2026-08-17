using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Slice.Authentication.Tests;

/// <summary>
/// Regression guard for the exact index-duplication bug this feature already shipped once:
/// demoting IdentityDbContext's base UserNameIndex/RoleNameIndex with an explicit index name creates
/// a SECOND, independent unique index instead of reconfiguring the base one in place — which would
/// silently keep cross-tenant duplicate usernames/role-names blocked even though the whole point of
/// tenant-scoped identity is to allow them. These tests fail if that regression reappears.
/// </summary>
public sealed class IndexUniquenessTests : AuthDbFixture
{
    [Fact]
    public async Task Same_username_across_different_tenants_is_allowed()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SliceAuthDbContext>();

        db.Users.Add(NewUser("shared@test.local", tenantA));
        db.Users.Add(NewUser("shared@test.local", tenantB));

        await db.SaveChangesAsync(); // must not throw
    }

    [Fact]
    public async Task Same_username_within_the_same_tenant_is_rejected()
    {
        var tenant = Guid.NewGuid();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SliceAuthDbContext>();

        db.Users.Add(NewUser("dup@test.local", tenant));
        await db.SaveChangesAsync();

        db.Users.Add(NewUser("dup@test.local", tenant));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Same_username_across_two_platform_tier_rows_is_rejected()
    {
        // The NULL-uniqueness gap: a single composite (TenantId, NormalizedUserName) index would NOT
        // catch this, since SQL treats every NULL as distinct. The second, NULL-filtered partial
        // index is what closes it for the platform tier specifically.
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SliceAuthDbContext>();

        db.Users.Add(NewUser("platform-admin@test.local", tenantId: null));
        await db.SaveChangesAsync();

        db.Users.Add(NewUser("platform-admin@test.local", tenantId: null));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Same_role_name_across_different_tenants_is_allowed()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SliceAuthDbContext>();

        db.Roles.Add(NewRole("Manager", tenantA));
        db.Roles.Add(NewRole("Manager", tenantB));

        await db.SaveChangesAsync(); // must not throw
    }

    [Fact]
    public async Task Same_role_name_within_the_same_tenant_is_rejected()
    {
        var tenant = Guid.NewGuid();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SliceAuthDbContext>();

        db.Roles.Add(NewRole("Manager", tenant));
        await db.SaveChangesAsync();

        db.Roles.Add(NewRole("Manager", tenant));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Same_role_name_across_two_platform_tier_rows_is_rejected()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SliceAuthDbContext>();

        db.Roles.Add(NewRole("admin", tenantId: null));
        await db.SaveChangesAsync();

        db.Roles.Add(NewRole("admin", tenantId: null));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static SliceUser NewUser(string email, Guid? tenantId) => new()
    {
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        TenantId = tenantId
    };

    private static SliceRole NewRole(string name, Guid? tenantId) => new()
    {
        Name = name,
        NormalizedName = name.ToUpperInvariant(),
        TenantId = tenantId
    };
}
