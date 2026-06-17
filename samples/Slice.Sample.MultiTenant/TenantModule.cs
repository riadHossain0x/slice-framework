using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using FluentValidation;
using Slice.AspNetCore;
using Slice.Core.Ambient;
using Slice.EntityFrameworkCore;
using Slice.Management;
using Slice.Mediator.Default;
using Slice.Modularity;
using Slice.MultiTenancy;

namespace Slice.Sample.MultiTenant;

/// <summary>
/// Design-time factory so <c>dotnet ef migrations add</c> can build <see cref="TenantDbContext"/>
/// without booting the app or an ambient tenant. Migrations are generated against the host database;
/// the ambient services passed here don't affect the generated schema (the tenant/soft-delete filters
/// are runtime query expressions).
/// </summary>
public sealed class TenantDbContextDesignTimeFactory : IDesignTimeDbContextFactory<TenantDbContext>
{
    public TenantDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite("Data Source=tenant-host.db")
            .Options;
        return new TenantDbContext(options, new NullCurrentTenant(), new DataFilter());
    }
}

/// <summary>
/// Database-per-tenant with the tenant→database map held in a <b>registry database</b> (not a
/// hard-coded dictionary): <c>ManagementTenantConnectionStore</c> reads each tenant's connection
/// string from the <c>SliceTenants</c> table (cached). Tenants can be onboarded at runtime via
/// <c>POST /api/tenants</c>, which registers the tenant and provisions its dedicated database.
/// </summary>
[DependsOn(typeof(SliceAspNetCoreModule), typeof(SliceEntityFrameworkCoreModule), typeof(SliceMultiTenancyModule))]
public sealed class TenantModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var services = context.Services;
        var assembly = typeof(TenantModule).Assembly;

        services.AddSliceMediator();
        services.AddRequestHandlers(assembly);
        services.AddValidatorsFromAssembly(assembly);
        services.AddSliceConventions(assembly);

        // The tenant registry (SliceTenants) is the source of truth for tenant→connection-string.
        services.AddSliceManagementStore(o => o.UseSqlite("Data Source=tenant-registry.db"));
        services.AddSliceManagementTenantConnectionStore();
        services.AddScoped<ITenantManager, TenantManager>();   // tenant CRUD over the registry (this sample doesn't load the full management module)

        // The per-tenant data context — its connection string is resolved per request from the registry.
        services.AddSliceMultiTenantDbContext<TenantDbContext>(
            defaultConnectionString: "Data Source=tenant-host.db",
            configure: (options, connectionString) => options.UseSqlite(connectionString));
        services.AddScoped<IWidgetRepository, EfWidgetRepository>();

        // Applies EF migrations to the host DB + every registered tenant DB (startup + onboarding).
        services.AddSliceTenantDatabaseMigrator<TenantDbContext>();

        // This sample only uses the management tenant *registry*, not its HTTP API — drop the
        // management module's controllers so the only endpoints are /api/widgets and /api/tenants.
        services.AddControllers().ConfigureApplicationPartManager(apm =>
        {
            var part = apm.ApplicationParts.FirstOrDefault(p => p.Name == "Slice.Management");
            if (part is not null) apm.ApplicationParts.Remove(part);
        });
    }

    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        var sp = context.ServiceProvider;

        // Ensure the registry, then seed the two demo tenants (id + name + dedicated DB) if absent.
        using (var scope = sp.CreateScope())
        {
            var registry = scope.ServiceProvider.GetRequiredService<SliceManagementDbContext>();
            await registry.Database.EnsureCreatedAsync();

            (Guid Id, string Name, string Cs)[] seeds =
            [
                (DemoTenants.TenantA, "Tenant A", "Data Source=tenant-a.db"),
                (DemoTenants.TenantB, "Tenant B", "Data Source=tenant-b.db"),
            ];
            foreach (var (id, name, cs) in seeds)
                if (!await registry.Tenants.AnyAsync(t => t.Id == id))
                    registry.Tenants.Add(new TenantRecord { Id = id, Name = name, ConnectionString = cs });
            await registry.SaveChangesAsync();
        }

        // Provision/upgrade the host DB and every registered tenant's DB by applying EF migrations.
        // (Replaces EnsureCreated: this writes __EFMigrationsHistory and evolves existing schemas.)
        // For large fleets / multiple replicas, set MultiTenant:RunMigrationsOnStartup=false and run the
        // separate Slice.Sample.MultiTenant.Migrator job instead (decouples migration from serving).
        var runOnStartup = sp.GetRequiredService<IConfiguration>().GetValue("MultiTenant:RunMigrationsOnStartup", true);
        if (runOnStartup)
            using (var scope = sp.CreateScope())
                await scope.ServiceProvider.GetRequiredService<ITenantDatabaseMigrator>().MigrateAllAsync();
    }
}
