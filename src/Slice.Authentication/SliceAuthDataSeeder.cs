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
        await db.Database.EnsureCreatedAsync();

        if (!options.SeedDemoAdmin)
            return;

        var roleManager = serviceProvider.GetRequiredService<RoleManager<SliceRole>>();
        var userManager = serviceProvider.GetRequiredService<UserManager<SliceUser>>();
        var permissions = serviceProvider.GetRequiredService<IPermissionDefinitionManager>();

        var role = await roleManager.FindByNameAsync(options.AdminRole);
        if (role is null)
        {
            role = new SliceRole { Name = options.AdminRole };
            await roleManager.CreateAsync(role);
        }

        var existing = (await roleManager.GetClaimsAsync(role))
            .Where(c => c.Type == SliceClaims.Permission)
            .Select(c => c.Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var permission in permissions.GetPermissions())
            if (existing.Add(permission.Name))
                await roleManager.AddClaimAsync(role, new Claim(SliceClaims.Permission, permission.Name));

        var user = await userManager.FindByEmailAsync(options.AdminEmail);
        if (user is null)
        {
            user = new SliceUser { UserName = options.AdminEmail, Email = options.AdminEmail, EmailConfirmed = true };
            var result = await userManager.CreateAsync(user, options.AdminPassword);
            if (result.Succeeded)
                await userManager.AddToRoleAsync(user, options.AdminRole);
        }
    }
}
