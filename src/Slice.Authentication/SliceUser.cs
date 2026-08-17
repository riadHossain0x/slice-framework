using Microsoft.AspNetCore.Identity;
using Slice.Domain;
using Slice.Domain.MultiTenancy;

namespace Slice.Authentication;

/// <summary>
/// Application user (Guid-keyed ASP.NET Identity), with an extra-properties JSON column.
/// Not sealed: <see cref="TenantId"/> makes this multi-tenant-capable — a null <c>TenantId</c> is a
/// platform/host identity, a non-null <c>TenantId</c> is in-tenant staff. Username/email uniqueness
/// is enforced per-(TenantId, NormalizedUserName) rather than globally — see
/// <see cref="SliceAuthDbContext"/>.OnModelCreating and <see cref="TenantScopedUserValidator"/>.
/// Because role names are no longer globally unique either once tenants exist, resolve roles via
/// <see cref="ITenantRoleAssigner"/>, not <c>RoleManager.FindByNameAsync</c> /
/// <c>UserManager.AddToRoleAsync</c> and friends — see docs/multitenancy.md ("Tenant-scoped identity").
/// </summary>
public class SliceUser : IdentityUser<Guid>, IHasExtraProperties, IMultiTenant
{
    public ExtraPropertyDictionary ExtraProperties { get; private set; } = new();
    public Guid? TenantId { get; set; }
}

/// <summary>
/// Application role; carries <c>permission</c> role-claims that flow into issued tokens. Not sealed:
/// <see cref="TenantId"/> makes roles tenant-scoped too — a role named "Manager" can exist
/// independently per tenant. See <see cref="SliceUser"/>'s remarks for the same uniqueness/
/// role-resolution caveats.
/// </summary>
public class SliceRole : IdentityRole<Guid>, IHasExtraProperties, IMultiTenant
{
    public ExtraPropertyDictionary ExtraProperties { get; private set; } = new();
    public Guid? TenantId { get; set; }
}
