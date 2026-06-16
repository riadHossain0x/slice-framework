using Microsoft.EntityFrameworkCore;
using Slice.Core.Ambient;
using Slice.Domain;
using Slice.EntityFrameworkCore;

namespace Slice.Management;

// ── Entities ─────────────────────────────────────────────────────────────────

/// <summary>A permission granted to a role ("R") or user ("U").</summary>
public sealed class PermissionGrant : IHasExtraProperties
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;        // permission name
    public string ProviderName { get; set; } = string.Empty; // "R" | "U"
    public string ProviderKey { get; set; } = string.Empty;  // role name / user id
    public ExtraPropertyDictionary ExtraProperties { get; private set; } = new();
}

/// <summary>A tenant record. <see cref="ConnectionString"/> is its dedicated database (null ⇒ shares the host DB).</summary>
public sealed class TenantRecord : IHasExtraProperties
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ConnectionString { get; set; }
    public ExtraPropertyDictionary ExtraProperties { get; private set; } = new();
}

/// <summary>A setting value scoped to a provider (global/tenant/user).</summary>
public sealed class SettingValueRecord : IHasExtraProperties
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Value { get; set; }
    public string ProviderName { get; set; } = string.Empty; // "G" | "T" | "U"
    public string? ProviderKey { get; set; }
    public ExtraPropertyDictionary ExtraProperties { get; private set; } = new();
}

/// <summary>A feature value scoped to a provider (global/tenant).</summary>
public sealed class FeatureValueRecord : IHasExtraProperties
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Value { get; set; }
    public string ProviderName { get; set; } = string.Empty; // "G" | "T"
    public string? ProviderKey { get; set; }
    public ExtraPropertyDictionary ExtraProperties { get; private set; } = new();
}

/// <summary>Shared store for the management modules (permissions, tenants, settings, features).</summary>
public sealed class SliceManagementDbContext(DbContextOptions<SliceManagementDbContext> options, ICurrentTenant currentTenant, IDataFilter dataFilter)
    : SliceDbContext(options, currentTenant, dataFilter)
{
    public DbSet<PermissionGrant> PermissionGrants => Set<PermissionGrant>();
    public DbSet<TenantRecord> Tenants => Set<TenantRecord>();
    public DbSet<SettingValueRecord> SettingValues => Set<SettingValueRecord>();
    public DbSet<FeatureValueRecord> FeatureValues => Set<FeatureValueRecord>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<PermissionGrant>(e =>
        {
            e.ToTable("SlicePermissionGrants");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProviderName, x.ProviderKey, x.Name }).IsUnique();
        });
        b.Entity<TenantRecord>(e =>
        {
            e.ToTable("SliceTenants");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Name).IsUnique();
        });
        b.Entity<SettingValueRecord>(e =>
        {
            e.ToTable("SliceSettingValues");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Name, x.ProviderName, x.ProviderKey }).IsUnique();
        });
        b.Entity<FeatureValueRecord>(e =>
        {
            e.ToTable("SliceFeatureValues");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Name, x.ProviderName, x.ProviderKey }).IsUnique();
        });

        base.OnModelCreating(b);
    }
}
