using Microsoft.EntityFrameworkCore;
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

        // The per-tenant data context — its connection string is resolved per request from the registry.
        services.AddSliceMultiTenantDbContext<TenantDbContext>(
            defaultConnectionString: "Data Source=tenant-host.db",
            configure: (options, connectionString) => options.UseSqlite(connectionString));
        services.AddScoped<IWidgetRepository, EfWidgetRepository>();

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

        // Provision the host DB and each seeded tenant's dedicated DB (resolve the context under each).
        var currentTenant = sp.GetRequiredService<ICurrentTenant>();
        foreach (var tenantId in new Guid?[] { null, DemoTenants.TenantA, DemoTenants.TenantB })
        {
            using (currentTenant.Change(tenantId))
            using (var scope = sp.CreateScope())
                await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Database.EnsureCreatedAsync();
        }
    }
}
