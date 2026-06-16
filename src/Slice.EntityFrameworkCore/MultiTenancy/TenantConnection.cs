using System.Collections.Concurrent;
using Slice.Core.Ambient;
using Slice.Core.DependencyInjection;

namespace Slice.EntityFrameworkCore.MultiTenancy;

/// <summary>
/// Maps a tenant to its dedicated database connection string. Returning <c>null</c> means the tenant
/// has no separate database and shares the host/default database (row-level isolation still applies).
/// </summary>
public interface ITenantConnectionStore
{
    string? Find(Guid tenantId);
}

/// <summary>Default store: no tenant has a dedicated database — everyone shares the host database.</summary>
public sealed class NullTenantConnectionStore : ITenantConnectionStore, ISingletonDependency
{
    public string? Find(Guid tenantId) => null;
}

/// <summary>
/// In-memory tenant→connection-string map for hosts that configure databases up front (or seed at
/// startup). Register via <see cref="TenantConnectionRegistration.AddTenantConnectionStrings"/>.
/// </summary>
public sealed class InMemoryTenantConnectionStore : ITenantConnectionStore
{
    private readonly ConcurrentDictionary<Guid, string> _map = new();

    public InMemoryTenantConnectionStore(IDictionary<Guid, string>? seed = null)
    {
        if (seed is null) return;
        foreach (var kvp in seed) _map[kvp.Key] = kvp.Value;
    }

    public void Set(Guid tenantId, string connectionString) => _map[tenantId] = connectionString;
    public string? Find(Guid tenantId) => _map.TryGetValue(tenantId, out var cs) ? cs : null;
}

/// <summary>
/// Resolves the connection string for the current tenant: the tenant's dedicated database if the
/// store has one, otherwise the per-context default (host) connection string.
/// </summary>
public interface ITenantConnectionResolver
{
    string Resolve(Guid? tenantId, string defaultConnectionString);
}

public sealed class TenantConnectionResolver(ITenantConnectionStore store) : ITenantConnectionResolver, ISingletonDependency
{
    public string Resolve(Guid? tenantId, string defaultConnectionString)
        => (tenantId is { } id ? store.Find(id) : null) ?? defaultConnectionString;
}
