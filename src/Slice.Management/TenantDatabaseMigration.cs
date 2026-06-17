using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Slice.Core.Ambient;

namespace Slice.Management;

/// <summary>Tuning for a fleet-wide migration run (<see cref="ITenantDatabaseMigrator.MigrateAllAsync(TenantMigrationOptions, CancellationToken)"/>).</summary>
public sealed class TenantMigrationOptions
{
    /// <summary>How many tenant databases to migrate concurrently. <c>1</c> (default) is sequential.</summary>
    public int MaxDegreeOfParallelism { get; init; } = 1;

    /// <summary>When <c>false</c> (default), the first tenant failure aborts the run; when <c>true</c>, every tenant is attempted and failures are collected in the report.</summary>
    public bool ContinueOnError { get; init; }

    /// <summary>Acquire <see cref="IDistributedLock"/> for the run so only one process migrates the fleet at a time. Default off (the no-op lock always acquires).</summary>
    public bool UseDistributedLock { get; init; }

    public string LockKey { get; init; } = "slice:tenant-db-migrations";

    public TimeSpan? LockTimeout { get; init; }
}

/// <summary>Outcome of migrating one database (<c>TenantId == null</c> ⇒ the host/default DB).</summary>
public sealed record TenantMigrationResult(Guid? TenantId, bool Migrated, string? Error);

/// <summary>Aggregate result of a fleet-wide migration run.</summary>
public sealed class TenantMigrationReport
{
    public IReadOnlyList<TenantMigrationResult> Results { get; init; } = [];

    /// <summary><c>true</c> when the distributed lock was held by another runner, so this run did nothing.</summary>
    public bool LockNotAcquired { get; init; }

    public int Succeeded => Results.Count(r => r.Migrated);
    public int Failed => Results.Count(r => !r.Migrated);
}

/// <summary>
/// Applies EF Core migrations across a database-per-tenant deployment: the host/default database plus
/// every tenant registered in the <c>SliceTenants</c> registry. Use it at startup (small fleets) or from
/// a dedicated migration job/executable (large fleets), and per-tenant when onboarding a new tenant.
/// </summary>
public interface ITenantDatabaseMigrator
{
    /// <summary>Migrates the host/default database and then every registered tenant's database, sequentially, failing fast.</summary>
    Task MigrateAllAsync(CancellationToken ct = default);

    /// <summary>Migrates the host/default database + every registered tenant per <paramref name="options"/> (parallelism, continue-on-error, single-runner lock), returning a per-tenant report.</summary>
    Task<TenantMigrationReport> MigrateAllAsync(TenantMigrationOptions options, CancellationToken ct = default);

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
    IServiceScopeFactory scopeFactory,
    IDistributedLock distributedLock) : ITenantDatabaseMigrator
    where TContext : DbContext
{
    public async Task MigrateAllAsync(CancellationToken ct = default)
    {
        // Sequential, fail-fast — preserves the original startup semantics.
        var report = await MigrateAllAsync(new TenantMigrationOptions(), ct);
        if (report.Failed > 0)
        {
            var first = report.Results.First(r => !r.Migrated);
            throw new InvalidOperationException(
                $"Migrating tenant '{first.TenantId?.ToString() ?? "(host)"}' failed: {first.Error}");
        }
    }

    public async Task<TenantMigrationReport> MigrateAllAsync(TenantMigrationOptions options, CancellationToken ct = default)
    {
        await using var handle = options.UseDistributedLock
            ? await distributedLock.TryAcquireAsync(options.LockKey, options.LockTimeout, ct)
            : null;

        if (options.UseDistributedLock && handle is null)
            return new TenantMigrationReport { LockNotAcquired = true };   // another runner owns the fleet migration

        var results = new ConcurrentBag<TenantMigrationResult>();

        // Host/default database first (sequential), so the registry/shared schema is current before tenants.
        var hostResult = await MigrateOneAsync(null, ct);
        results.Add(hostResult);
        if (!hostResult.Migrated && !options.ContinueOnError)
            return Report(results);

        var ids = await registry.Tenants.AsNoTracking().Select(t => t.Id).ToListAsync(ct);

        using var failFast = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var parallel = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, options.MaxDegreeOfParallelism),
            CancellationToken = failFast.Token,
        };

        try
        {
            await Parallel.ForEachAsync(ids, parallel, async (id, token) =>
            {
                var result = await MigrateOneAsync(id, token);
                results.Add(result);
                if (!result.Migrated && !options.ContinueOnError)
                    await failFast.CancelAsync();   // stop the remaining tenants
            });
        }
        catch (OperationCanceledException) when (failFast.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Fail-fast: a tenant failed and cancelled the rest. The failure is recorded in `results`.
        }

        return Report(results);

        static TenantMigrationReport Report(ConcurrentBag<TenantMigrationResult> bag) =>
            new() { Results = bag.OrderBy(r => r.TenantId).ToList() };
    }

    public Task MigrateTenantAsync(Guid? tenantId, CancellationToken ct = default) =>
        RunMigrateAsync(tenantId, ct);

    private async Task<TenantMigrationResult> MigrateOneAsync(Guid? tenantId, CancellationToken ct)
    {
        try
        {
            await RunMigrateAsync(tenantId, ct);
            return new TenantMigrationResult(tenantId, Migrated: true, Error: null);
        }
        catch (OperationCanceledException)
        {
            throw;   // propagate cancellation (fail-fast / host shutdown), don't record as a tenant failure
        }
        catch (Exception ex)
        {
            return new TenantMigrationResult(tenantId, Migrated: false, Error: ex.Message);
        }
    }

    private async Task RunMigrateAsync(Guid? tenantId, CancellationToken ct)
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
