using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Slice.Core.DependencyInjection;

namespace Slice.Authentication;

/// <summary>
/// Tenant-safe replacements for the handful of UserManager/RoleManager role-by-NAME APIs that
/// assume role names are globally unique. They no longer are: two tenants may each have a role
/// named "Manager". Every stock by-name API (FindByNameAsync, AddToRoleAsync, AddToRolesAsync,
/// RemoveFromRoleAsync, RemoveFromRolesAsync, IsInRoleAsync, GetUsersInRoleAsync, ...) does a bare
/// NormalizedName lookup with no tenant filter and throws (or silently picks the wrong role) once
/// that happens. Use this instead anywhere a role is resolved by name.
///
/// Every by-name member here normalizes through <c>RoleManager.KeyNormalizer</c> and matches on
/// <c>NormalizedName</c>, so lookups are case-insensitive in the same way the stock APIs are.
/// </summary>
public interface ITenantRoleAssigner
{
    Task<SliceRole?> FindRoleAsync(string roleName, Guid? tenantId, CancellationToken ct = default);
    Task<List<SliceRole>> FindRolesAsync(IEnumerable<string> roleNames, Guid? tenantId, CancellationToken ct = default);

    /// <summary>
    /// The roles this user is actually linked to, resolved through the UserRoles join rather than by
    /// re-looking-up role names. Prefer this over <see cref="FindRolesAsync"/> when you want "this
    /// user's roles": it is exact even when a user is deliberately linked to a role outside their own
    /// tenant (e.g. a platform-tier support role granted to in-tenant staff), which a name+tenant
    /// re-lookup would silently drop.
    /// </summary>
    Task<List<SliceRole>> GetRolesOfUserAsync(SliceUser user, CancellationToken ct = default);

    Task<IdentityResult> AddToRoleAsync(SliceUser user, string roleName, CancellationToken ct = default);
    Task<IdentityResult> AddToRolesAsync(SliceUser user, IEnumerable<string> roleNames, CancellationToken ct = default);
    Task<IdentityResult> RemoveFromRoleAsync(SliceUser user, string roleName, CancellationToken ct = default);
    Task<IdentityResult> RemoveFromRolesAsync(SliceUser user, IEnumerable<string> roleNames, CancellationToken ct = default);
    Task<bool> IsInRoleAsync(SliceUser user, string roleName, CancellationToken ct = default);
    Task<List<SliceUser>> GetUsersInRoleAsync(SliceRole role, CancellationToken ct = default);
    Task<int> CountUsersInRoleAsync(SliceRole role, CancellationToken ct = default);
}

/// <inheritdoc cref="ITenantRoleAssigner"/>
/// <remarks>
/// Takes the concrete <see cref="SliceAuthDbContext"/>, not a derived type. A host that subclasses it
/// (<c>AppAuthDbContext : SliceAuthDbContext</c>) must still register the base type for this to
/// resolve — e.g. <c>services.AddScoped&lt;SliceAuthDbContext&gt;(sp =&gt; sp.GetRequiredService&lt;AppAuthDbContext&gt;())</c>.
///
/// Writes go straight to the join table and are committed here rather than deferred to
/// <c>UnitOfWorkBehavior</c>: <see cref="SliceAuthDbContext"/> is an <c>IdentityDbContext</c>, not a
/// <c>SliceDbContext</c>, so it is not registered as an <c>IUnitOfWork</c> and nothing else would
/// commit them. Each method saves at most once, so a partial failure can't leave half a batch behind.
/// </remarks>
public sealed class TenantRoleAssigner(RoleManager<SliceRole> roleManager, SliceAuthDbContext db)
    : ITenantRoleAssigner, IScopedDependency
{
    public async Task<SliceRole?> FindRoleAsync(string roleName, Guid? tenantId, CancellationToken ct = default)
    {
        var normalized = Normalize(roleName);
        if (normalized is null) return null;

        // Take(2) rather than SingleOrDefaultAsync — the same two rows either way, but a duplicate
        // becomes a diagnosable fault instead of a bare "Sequence contains more than one element".
        // It is reachable: the DB-level guarantee behind this lookup is a pair of partial unique
        // indexes, and TenantIndexFilters cannot create those on providers without partial-index
        // support (MySQL/MariaDB, Oracle), which leaves the platform tier (TenantId IS NULL)
        // protected only by TenantScopedRoleValidator's read-then-write check.
        var matches = await roleManager.Roles
            .Where(r => r.NormalizedName == normalized && r.TenantId == tenantId)
            .OrderBy(r => r.Id)
            .Take(2)
            .ToListAsync(ct);

        if (matches.Count > 1)
            throw new InvalidOperationException(
                $"More than one role is named '{roleName}' in " +
                (tenantId is null ? "the platform tier" : $"tenant '{tenantId}'") +
                ". Role names must be unique per tenant — see docs/multitenancy.md " +
                "(\"Tenant-scoped identity\"). Deduplicate the rows, and on a provider without " +
                "partial-index support add your own equivalent constraint.");

        return matches.Count == 0 ? null : matches[0];
    }

    public Task<List<SliceRole>> FindRolesAsync(IEnumerable<string> roleNames, Guid? tenantId, CancellationToken ct = default)
        => FindRolesAsync(NormalizeAll(roleNames), tenantId, ct);

    // Normalized, to match FindRoleAsync — comparing raw Name would be case- and collation-sensitive,
    // so the two would disagree about the same role.
    private Task<List<SliceRole>> FindRolesAsync(List<string> normalizedNames, Guid? tenantId, CancellationToken ct)
        => normalizedNames.Count == 0
            ? Task.FromResult(new List<SliceRole>())
            : roleManager.Roles
                .Where(r => r.TenantId == tenantId && normalizedNames.Contains(r.NormalizedName!))
                .ToListAsync(ct);

    public Task<List<SliceRole>> GetRolesOfUserAsync(SliceUser user, CancellationToken ct = default)
        => db.UserRoles
            .Where(ur => ur.UserId == user.Id)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r)
            .ToListAsync(ct);

    public Task<IdentityResult> AddToRoleAsync(SliceUser user, string roleName, CancellationToken ct = default)
        => AddToRolesAsync(user, [roleName], ct);

    public async Task<IdentityResult> AddToRolesAsync(SliceUser user, IEnumerable<string> roleNames, CancellationToken ct = default)
    {
        // Resolve every name up front so an unknown one fails the whole call without having written
        // any of the others — assigning a partial set of roles is worse than assigning none.
        var names = roleNames as IReadOnlyCollection<string> ?? [.. roleNames];
        if (names.Count == 0) return IdentityResult.Success;

        var roles = await FindRolesAsync(NormalizeAll(names), user.TenantId, ct);
        if (Missing(names, roles) is { } missing) return missing;

        var existingIds = (await db.UserRoles
            .Where(ur => ur.UserId == user.Id)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct)).ToHashSet();

        var added = false;
        foreach (var role in roles.Where(r => existingIds.Add(r.Id)))
        {
            db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = role.Id });
            added = true;
        }

        if (added) await db.SaveChangesAsync(ct);
        return IdentityResult.Success;
    }

    public Task<IdentityResult> RemoveFromRoleAsync(SliceUser user, string roleName, CancellationToken ct = default)
        => RemoveFromRolesAsync(user, [roleName], ct);

    public async Task<IdentityResult> RemoveFromRolesAsync(SliceUser user, IEnumerable<string> roleNames, CancellationToken ct = default)
    {
        var names = roleNames as IReadOnlyCollection<string> ?? [.. roleNames];
        if (names.Count == 0) return IdentityResult.Success;

        // Report an unknown role rather than reporting success for a removal that removed nothing —
        // a silent no-op here reads as "the user no longer has that role", which may not be true.
        var roles = await FindRolesAsync(NormalizeAll(names), user.TenantId, ct);
        if (Missing(names, roles) is { } missing) return missing;

        var roleIds = roles.Select(r => r.Id).ToHashSet();
        var links = await db.UserRoles
            .Where(ur => ur.UserId == user.Id && roleIds.Contains(ur.RoleId))
            .ToListAsync(ct);

        if (links.Count == 0) return IdentityResult.Success;

        db.UserRoles.RemoveRange(links);
        await db.SaveChangesAsync(ct);
        return IdentityResult.Success;
    }

    /// <summary>
    /// Whether the user is linked to a role with this name. Resolved by link, like
    /// <see cref="GetRolesOfUserAsync"/> — deliberately NOT by <c>(name, user.TenantId)</c>, which
    /// would answer false for a role granted from outside the user's own tenant even though that role
    /// is on their token and <c>[Authorize(Roles = ...)]</c> honors it. Two ways of asking the same
    /// question must not disagree, least of all the authorization-shaped one.
    /// </summary>
    public async Task<bool> IsInRoleAsync(SliceUser user, string roleName, CancellationToken ct = default)
    {
        var normalized = Normalize(roleName);
        return normalized is not null
            && await db.UserRoles
                .Where(ur => ur.UserId == user.Id)
                .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r)
                .AnyAsync(r => r.NormalizedName == normalized, ct);
    }

    public Task<List<SliceUser>> GetUsersInRoleAsync(SliceRole role, CancellationToken ct = default)
        => db.UserRoles
            .Where(ur => ur.RoleId == role.Id)
            .Join(db.Users, ur => ur.UserId, u => u.Id, (_, u) => u)
            .ToListAsync(ct);

    public Task<int> CountUsersInRoleAsync(SliceRole role, CancellationToken ct = default)
        => db.UserRoles.CountAsync(ur => ur.RoleId == role.Id, ct);

    private string? Normalize(string roleName) => roleManager.KeyNormalizer.NormalizeName(roleName);

    private List<string> NormalizeAll(IEnumerable<string> roleNames) =>
        [.. roleNames.Select(Normalize).OfType<string>().Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// A <c>RoleNotFound</c> failure naming the roles that didn't resolve, or null if all did. Names
    /// them as the caller wrote them rather than in normalized form, so the message is readable.
    /// </summary>
    private IdentityResult? Missing(IEnumerable<string> requestedNames, List<SliceRole> found)
    {
        // Nulls are reachable on both sides — SliceRole.NormalizedName is nullable and
        // KeyNormalizer.NormalizeName(null) returns null — and are handled explicitly rather than left
        // to HashSet's null fast-path. That fast-path does the right thing today (Contains(null) is
        // false without ever consulting StringComparer.Ordinal, which would itself throw on a null),
        // but relying on it makes the correctness of a null name silently dependent on the lookup
        // being a HashSet. Same OfType<string>() filter NormalizeAll already applies.
        var resolved = found.Select(r => r.NormalizedName).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var absent = requestedNames
            .Where(n => Normalize(n) is not { } normalized || !resolved.Contains(normalized))
            .Distinct()
            .ToList();
        if (absent.Count == 0) return null;

        return IdentityResult.Failed(new IdentityError
        {
            Code = "RoleNotFound",
            Description = $"Role '{string.Join("', '", absent.Select(n => n ?? "(null)"))}' does not exist for this tenant."
        });
    }
}
