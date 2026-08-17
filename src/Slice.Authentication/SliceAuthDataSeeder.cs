using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Slice.Authorization;

namespace Slice.Authentication;

/// <summary>
/// Seeds the identity store on startup: ensures the schema, creates the admin role granted every
/// declared permission (as <c>permission</c> role-claims), and a demo admin user in that role.
/// </summary>
public static class SliceAuthDataSeeder
{
    public static async Task SeedAsync(IServiceProvider serviceProvider)
    {
        var options = serviceProvider.GetRequiredService<SliceAuthOptions>();
        var db = serviceProvider.GetRequiredService<SliceAuthDbContext>();

        // Skipped when the host owns the schema (its own migrations). EnsureCreated is a no-op once
        // the database exists, so leaving it on there would quietly do nothing while looking like
        // schema management. See SliceAuthOptions.AutoCreateSchema.
        if (options.AutoCreateSchema)
            await db.Database.EnsureCreatedAsync();

        if (!options.SeedDemoAdmin)
            return;

        var roleManager = serviceProvider.GetRequiredService<RoleManager<SliceRole>>();
        var userManager = serviceProvider.GetRequiredService<UserManager<SliceUser>>();
        var permissions = serviceProvider.GetRequiredService<IPermissionDefinitionManager>();
        var tenantRoleAssigner = serviceProvider.GetRequiredService<ITenantRoleAssigner>();

        // This seeded role/user are always platform-tier (TenantId == null) — the demo admin isn't
        // scoped to any tenant. Resolved by (TenantId == null, NormalizedName) rather than
        // FindByNameAsync, since role names are no longer globally unique once tenants exist.
        var role = await roleManager.Roles.SingleOrDefaultAsync(
            r => r.NormalizedName == roleManager.KeyNormalizer.NormalizeName(options.AdminRole) && r.TenantId == null);
        if (role is null)
        {
            role = new SliceRole { Name = options.AdminRole, TenantId = null };
            await roleManager.CreateAsync(role);
        }

        var existing = (await roleManager.GetClaimsAsync(role))
            .Where(c => c.Type == SliceClaims.Permission)
            .Select(c => c.Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var permission in permissions.GetPermissions())
            if (existing.Add(permission.Name))
                await roleManager.AddClaimAsync(role, new Claim(SliceClaims.Permission, permission.Name));

        // Same rationale — resolve by (TenantId == null, NormalizedUserName), not FindByEmailAsync,
        // since usernames are no longer globally unique once tenants exist. The demo admin's
        // UserName is its email address, so the address is normalized as a user name here.
        var normalizedUserName = userManager.NormalizeName(options.AdminEmail);
        var user = await userManager.Users.SingleOrDefaultAsync(
            u => u.NormalizedUserName == normalizedUserName && u.TenantId == null);
        if (user is null)
        {
            user = new SliceUser { UserName = options.AdminEmail, Email = options.AdminEmail, EmailConfirmed = true, TenantId = null };
            var result = await userManager.CreateAsync(user, options.AdminPassword);
            if (result.Succeeded)
                await tenantRoleAssigner.AddToRoleAsync(user, options.AdminRole);
        }
    }
}
