using Microsoft.EntityFrameworkCore;
using Slice.Authorization;
using Slice.Core.Ambient;
using Slice.Core.DependencyInjection;

namespace Slice.Management;

public static class PermissionProviders
{
    public const string Role = "R";
    public const string User = "U";
}

/// <summary>
/// DB-backed <see cref="IPermissionStore"/>: a permission is granted when there's a grant for the
/// current user ("U") or any of their roles ("R"). Replaces the claims-only store from auth.
/// </summary>
public sealed class PermissionManagementStore(SliceManagementDbContext db, ICurrentUser currentUser)
    : IPermissionStore, IScopedDependency
{
    public async Task<bool> IsGrantedAsync(string permission, CancellationToken ct = default)
    {
        var userKey = currentUser.Id?.ToString();
        var roles = currentUser.Roles;
        if (userKey is null && roles.Length == 0)
            return false;

        return await db.PermissionGrants.AnyAsync(g =>
            g.Name == permission &&
            ((g.ProviderName == PermissionProviders.User && g.ProviderKey == userKey) ||
             (g.ProviderName == PermissionProviders.Role && roles.Contains(g.ProviderKey))), ct);
    }
}

/// <summary>Manages permission grants (used by the admin API + seeding).</summary>
public interface IPermissionGrantManager
{
    Task<IReadOnlyList<string>> GetGrantedAsync(string providerName, string providerKey, CancellationToken ct = default);
    Task GrantAsync(string providerName, string providerKey, string permission, CancellationToken ct = default);
    Task RevokeAsync(string providerName, string providerKey, string permission, CancellationToken ct = default);
}

public sealed class PermissionGrantManager(SliceManagementDbContext db, IGuidGenerator guids)
    : IPermissionGrantManager, IScopedDependency
{
    public async Task<IReadOnlyList<string>> GetGrantedAsync(string providerName, string providerKey, CancellationToken ct = default)
        => await db.PermissionGrants
            .Where(g => g.ProviderName == providerName && g.ProviderKey == providerKey)
            .Select(g => g.Name)
            .ToListAsync(ct);

    public async Task GrantAsync(string providerName, string providerKey, string permission, CancellationToken ct = default)
    {
        var exists = await db.PermissionGrants.AnyAsync(
            g => g.ProviderName == providerName && g.ProviderKey == providerKey && g.Name == permission, ct);
        if (exists) return;

        db.PermissionGrants.Add(new PermissionGrant
        {
            Id = guids.Create(),
            Name = permission,
            ProviderName = providerName,
            ProviderKey = providerKey
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task RevokeAsync(string providerName, string providerKey, string permission, CancellationToken ct = default)
    {
        await db.PermissionGrants
            .Where(g => g.ProviderName == providerName && g.ProviderKey == providerKey && g.Name == permission)
            .ExecuteDeleteAsync(ct);
    }
}
