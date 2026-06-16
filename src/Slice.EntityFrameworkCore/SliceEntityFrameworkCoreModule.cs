using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Slice.Application;
using Slice.Application.UnitOfWork;
using Slice.Core.Ambient;
using Slice.EntityFrameworkCore.Interceptors;
using Slice.EntityFrameworkCore.MultiTenancy;
using Slice.EntityFrameworkCore.Outbox;
using Slice.EventBus;
using Slice.Modularity;

namespace Slice.EntityFrameworkCore;

/// <summary>
/// Persistence module: registers the save-changes interceptors (auditing, domain-event dispatch)
/// and the local event bus. Feature modules call <see cref="AddSliceDbContext{TContext}"/>.
/// </summary>
[DependsOn(typeof(SliceApplicationModule))]
public sealed class SliceEntityFrameworkCoreModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddSliceConventions(typeof(SliceAuditingInterceptor).Assembly);
        context.Services.AddSliceConventions(typeof(LocalEventBus).Assembly);
    }
}

public static class SliceDbContextRegistration
{
    /// <summary>
    /// Registers a <see cref="SliceDbContext"/>, wires the framework interceptors, and exposes it
    /// as an <see cref="IUnitOfWork"/> so the unit-of-work behavior flushes it after a command.
    /// </summary>
    public static IServiceCollection AddSliceDbContext<TContext>(
        this IServiceCollection services, Action<DbContextOptionsBuilder> configure)
        where TContext : SliceDbContext
    {
        services.AddDbContext<TContext>((sp, options) =>
        {
            configure(options);
            AddInterceptors(sp, options);
        });
        return WireUnitOfWork<TContext>(services);
    }

    /// <summary>
    /// Same as <see cref="AddSliceDbContext{TContext}(IServiceCollection, Action{DbContextOptionsBuilder})"/>
    /// but the configure callback receives the <see cref="IServiceProvider"/> — use it to pull a shared
    /// resource (e.g. <c>sp.GetRequiredService&lt;NpgsqlDataSource&gt;()</c>) into the provider options so
    /// the context reuses the application's connection pool.
    /// </summary>
    public static IServiceCollection AddSliceDbContext<TContext>(
        this IServiceCollection services, Action<IServiceProvider, DbContextOptionsBuilder> configure)
        where TContext : SliceDbContext
    {
        services.AddDbContext<TContext>((sp, options) =>
        {
            configure(sp, options);
            AddInterceptors(sp, options);
        });
        return WireUnitOfWork<TContext>(services);
    }

    /// <summary>
    /// Registers a database-per-tenant <see cref="SliceDbContext"/>. The connection string is
    /// resolved per scope from the current tenant: if <see cref="ITenantConnectionStore"/> has a
    /// dedicated database for the tenant it is used, otherwise <paramref name="defaultConnectionString"/>
    /// (the host/shared database) is used. <paramref name="configure"/> applies the resolved string to
    /// the provider (e.g. <c>(o, cs) =&gt; o.UseSqlite(cs)</c>).
    /// </summary>
    public static IServiceCollection AddSliceMultiTenantDbContext<TContext>(
        this IServiceCollection services,
        string defaultConnectionString,
        Action<DbContextOptionsBuilder, string> configure)
        where TContext : SliceDbContext
    {
        services.AddDbContext<TContext>((sp, options) =>
        {
            var tenantId = sp.GetRequiredService<ICurrentTenant>().Id;
            var connectionString = sp.GetRequiredService<ITenantConnectionResolver>()
                .Resolve(tenantId, defaultConnectionString);
            configure(options, connectionString);
            AddInterceptors(sp, options);
        }, optionsLifetime: ServiceLifetime.Scoped);   // resolve the tenant connection per scope/request

        return WireUnitOfWork<TContext>(services);
    }

    private static void AddInterceptors(IServiceProvider sp, DbContextOptionsBuilder options)
        => options.AddInterceptors(
            sp.GetRequiredService<SliceAuditingInterceptor>(),
            sp.GetRequiredService<DomainEventInterceptor>());

    private static IServiceCollection WireUnitOfWork<TContext>(IServiceCollection services)
        where TContext : SliceDbContext
    {
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<TContext>());

        // Transactional-outbox delivery loop for this context.
        services.AddHostedService(sp => new OutboxProcessor<TContext>(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<OutboxProcessor<TContext>>()));

        return services;
    }
}

public static class TenantConnectionRegistration
{
    /// <summary>Registers an in-memory tenant→connection-string map as the active connection store.</summary>
    public static IServiceCollection AddTenantConnectionStrings(
        this IServiceCollection services, IDictionary<Guid, string> map)
    {
        services.RemoveAll<ITenantConnectionStore>();
        services.AddSingleton<ITenantConnectionStore>(new InMemoryTenantConnectionStore(map));
        return services;
    }
}
