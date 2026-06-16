using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Slice.Core.Ambient;

namespace Slice.Management;

/// <summary>
/// Applies EF Core migrations across a database-per-tenant deployment: the host/default database plus
/// every tenant registered in the <c>SliceTenants</c> registry. Use it at startup (and after a deploy
/// that adds migrations) to evolve every tenant database, and per-tenant when onboarding a new tenant.
/// </summary>
public interface ITenantDatabaseMigrator
{
    /// <summary>Migrates the host/default database and then every registered tenant's database.</summary>
    Task MigrateAllAsync(CancellationToken ct = default);

    /// <summary>Migrates a single tenant's database (<paramref name="tenantId"/> <c>null</c> ⇒ the host/default DB).</summary>
    Task MigrateTenantAsync(Guid? tenantId, CancellationToken ct = default);
}

/// <summary>
/// Default <see cref="ITenantDatabaseMigrator"/>. Resolves <typeparamref name="TContext"/> inside a child
/// scope under each tenant (via <see cref="ICurrentTenant.Change"/>) so the multi-tenant connection
/// resolver hands it that tenant's connection string, then calls <c>Database.MigrateAsync()</c>.
/// </summary>
public sealed class TenantDatabaseMigrator<TContext>(
    SliceManagementDbContext registry,
    ICurrentTenant currentTenant,
    IServiceScopeFactory scopeFactory) : ITenantDatabaseMigrator
    where TContext : DbContext
{
    public async Task MigrateAllAsync(CancellationToken ct = default)
    {
        await MigrateTenantAsync(null, ct);   // host/default database

        var ids = await registry.Tenants.AsNoTracking().Select(t => t.Id).ToListAsync(ct);
        foreach (var id in ids)
            await MigrateTenantAsync(id, ct);
    }

    public async Task MigrateTenantAsync(Guid? tenantId, CancellationToken ct = default)
    {
        using (currentTenant.Change(tenantId))
        using (var scope = scopeFactory.CreateScope())
            await scope.ServiceProvider.GetRequiredService<TContext>().Database.MigrateAsync(ct);
    }
}

public static class TenantDatabaseMigratorRegistration
{
    /// <summary>
    /// Registers <see cref="ITenantDatabaseMigrator"/> backed by <typeparamref name="TContext"/> — the
    /// per-tenant data context registered with <c>AddSliceMultiTenantDbContext</c>.
    /// </summary>
    public static IServiceCollection AddSliceTenantDatabaseMigrator<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.AddScoped<ITenantDatabaseMigrator, TenantDatabaseMigrator<TContext>>();
        return services;
    }
}
