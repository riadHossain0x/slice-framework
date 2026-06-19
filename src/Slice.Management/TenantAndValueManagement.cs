using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Slice.Core.Ambient;
using Slice.Core.DependencyInjection;
using Slice.EntityFrameworkCore.MultiTenancy;
using Slice.Features;
using Slice.Settings;

namespace Slice.Management;

// ── Tenants ──────────────────────────────────────────────────────────────────
public interface ITenantManager
{
    Task<TenantRecord> CreateAsync(string name, string? connectionString = null, CancellationToken ct = default);
    Task<IReadOnlyList<TenantRecord>> GetListAsync(CancellationToken ct = default);
    Task<TenantRecord?> FindByNameAsync(string name, CancellationToken ct = default);
    Task<TenantRecord?> FindByIdAsync(Guid id, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class TenantManager(SliceManagementDbContext db, IGuidGenerator guids) : ITenantManager, IScopedDependency
{
    public async Task<TenantRecord> CreateAsync(string name, string? connectionString = null, CancellationToken ct = default)
    {
        var tenant = new TenantRecord { Id = guids.Create(), Name = name, ConnectionString = connectionString };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(ct);
        return tenant;
    }

    public async Task<IReadOnlyList<TenantRecord>> GetListAsync(CancellationToken ct = default)
        => await db.Tenants.OrderBy(t => t.Name).ToListAsync(ct);

    public Task<TenantRecord?> FindByNameAsync(string name, CancellationToken ct = default)
        => db.Tenants.FirstOrDefaultAsync(t => t.Name == name, ct);

    public Task<TenantRecord?> FindByIdAsync(Guid id, CancellationToken ct = default)
        => db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
        => await db.Tenants.Where(t => t.Id == id).ExecuteDeleteAsync(ct);
}

/// <summary>
/// A production-shaped <see cref="ITenantConnectionStore"/>: it resolves each tenant's dedicated
/// database connection string from the <c>SliceTenants</c> registry rather than a hard-coded map.
/// Results are cached in memory (the resolver runs per request); the cache fills lazily on first use
/// and reloads on miss, so tenants onboarded elsewhere are picked up. Call <see cref="Invalidate"/>
/// after changing a tenant's connection string in-process.
/// </summary>
public sealed class ManagementTenantConnectionStore(IServiceScopeFactory scopeFactory) : ITenantConnectionStore
{
    private readonly ConcurrentDictionary<Guid, string?> _cache = new();

    public string? Find(Guid tenantId)
    {
        if (_cache.TryGetValue(tenantId, out var cached))
            return cached;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SliceManagementDbContext>();
        var connectionString = db.Tenants.AsNoTracking().FirstOrDefault(t => t.Id == tenantId)?.ConnectionString;
        _cache[tenantId] = connectionString;   // caches the negative (null) result too
        return connectionString;
    }

    public void Invalidate(Guid tenantId) => _cache.TryRemove(tenantId, out _);
}

public static class ManagementTenantConnectionRegistration
{
    /// <summary>
    /// Uses the <c>SliceTenants</c> registry (via <see cref="ManagementTenantConnectionStore"/>) as the
    /// source of per-tenant connection strings for <c>AddSliceMultiTenantDbContext</c>. Requires the
    /// management store (<c>AddSliceManagementStore</c>) to be registered.
    /// </summary>
    public static IServiceCollection AddSliceManagementTenantConnectionStore(this IServiceCollection services)
    {
        services.RemoveAll<ITenantConnectionStore>();
        services.AddSingleton<ManagementTenantConnectionStore>();
        services.AddSingleton<ITenantConnectionStore>(sp => sp.GetRequiredService<ManagementTenantConnectionStore>());
        return services;
    }
}

// ── Setting values (DB-backed, highest-priority provider) ────────────────────
public sealed class ManagementSettingValueProvider(SliceManagementDbContext db, ICurrentUser user, ICurrentTenant tenant)
    : ISettingValueProvider, IScopedDependency
{
    public int Order => -10; // above global-override/config/default

    public async Task<string?> GetOrNullAsync(SettingDefinition setting)
    {
        var rows = await db.SettingValues.Where(s => s.Name == setting.Name).ToListAsync();
        string? Pick(string pn, string? pk) => rows.FirstOrDefault(r => r.ProviderName == pn && r.ProviderKey == pk)?.Value;
        return (user.Id is { } uid ? Pick("U", uid.ToString()) : null)
            ?? (tenant.Id is { } tid ? Pick("T", tid.ToString()) : null)
            ?? Pick("G", null);
    }
}

// ── Feature values (DB-backed, falls back to configuration) ──────────────────
public sealed class ManagementFeatureStore(SliceManagementDbContext db, ICurrentTenant tenant, IConfiguration configuration)
    : IFeatureStore, IScopedDependency
{
    public async Task<string?> GetOrNullAsync(string name)
    {
        var rows = await db.FeatureValues.Where(f => f.Name == name).ToListAsync();
        string? Pick(string pn, string? pk) => rows.FirstOrDefault(r => r.ProviderName == pn && r.ProviderKey == pk)?.Value;
        return (tenant.Id is { } tid ? Pick("T", tid.ToString()) : null)
            ?? Pick("G", null)
            ?? configuration[$"Features:{name}"];
    }
}

// ── Setting-value administration (write side for SliceSettingValues) ─────────
/// <summary>Manages DB-backed setting values per scope ("G" global / "T" tenant / "U" user).</summary>
public interface ISettingValueManager
{
    Task SetAsync(string name, string? value, string providerName, string? providerKey, CancellationToken ct = default);
    Task ClearAsync(string name, string providerName, string? providerKey, CancellationToken ct = default);
    Task<string?> GetAsync(string name, string providerName, string? providerKey, CancellationToken ct = default);
}

public sealed class SettingValueManager(SliceManagementDbContext db, IGuidGenerator guids)
    : ISettingValueManager, IScopedDependency
{
    public async Task SetAsync(string name, string? value, string providerName, string? providerKey, CancellationToken ct = default)
    {
        var row = await db.SettingValues.FirstOrDefaultAsync(
            s => s.Name == name && s.ProviderName == providerName && s.ProviderKey == providerKey, ct);
        if (row is null)
            db.SettingValues.Add(new SettingValueRecord
            { Id = guids.Create(), Name = name, Value = value, ProviderName = providerName, ProviderKey = providerKey });
        else
            row.Value = value;
        await db.SaveChangesAsync(ct);
    }

    public async Task ClearAsync(string name, string providerName, string? providerKey, CancellationToken ct = default)
        => await db.SettingValues
            .Where(s => s.Name == name && s.ProviderName == providerName && s.ProviderKey == providerKey)
            .ExecuteDeleteAsync(ct);

    public Task<string?> GetAsync(string name, string providerName, string? providerKey, CancellationToken ct = default)
        => db.SettingValues
            .Where(s => s.Name == name && s.ProviderName == providerName && s.ProviderKey == providerKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
}

// ── Feature-value administration (write side for SliceFeatureValues) ─────────
/// <summary>Manages DB-backed feature values per scope ("G" global / "T" tenant).</summary>
public interface IFeatureValueManager
{
    Task SetAsync(string name, string? value, string providerName, string? providerKey, CancellationToken ct = default);
    Task ClearAsync(string name, string providerName, string? providerKey, CancellationToken ct = default);
    Task<string?> GetAsync(string name, string providerName, string? providerKey, CancellationToken ct = default);
}

public sealed class FeatureValueManager(SliceManagementDbContext db, IGuidGenerator guids)
    : IFeatureValueManager, IScopedDependency
{
    public async Task SetAsync(string name, string? value, string providerName, string? providerKey, CancellationToken ct = default)
    {
        var row = await db.FeatureValues.FirstOrDefaultAsync(
            f => f.Name == name && f.ProviderName == providerName && f.ProviderKey == providerKey, ct);
        if (row is null)
            db.FeatureValues.Add(new FeatureValueRecord
            { Id = guids.Create(), Name = name, Value = value, ProviderName = providerName, ProviderKey = providerKey });
        else
            row.Value = value;
        await db.SaveChangesAsync(ct);
    }

    public async Task ClearAsync(string name, string providerName, string? providerKey, CancellationToken ct = default)
        => await db.FeatureValues
            .Where(f => f.Name == name && f.ProviderName == providerName && f.ProviderKey == providerKey)
            .ExecuteDeleteAsync(ct);

    public Task<string?> GetAsync(string name, string providerName, string? providerKey, CancellationToken ct = default)
        => db.FeatureValues
            .Where(f => f.Name == name && f.ProviderName == providerName && f.ProviderKey == providerKey)
            .Select(f => f.Value)
            .FirstOrDefaultAsync(ct);
}
