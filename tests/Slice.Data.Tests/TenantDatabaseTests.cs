using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Slice.Core.Ambient;
using Slice.EntityFrameworkCore;
using Slice.Modularity;
using Slice.MultiTenancy;

namespace Slice.Data.Tests;

[DependsOn(typeof(SliceMultiTenancyModule), typeof(SliceEntityFrameworkCoreModule))]
public sealed class TenantDbTestModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var host = context.Configuration.GetConnectionString("Host")!;
        context.Services.AddSliceMultiTenantDbContext<TestDbContext>(host, (o, cs) => o.UseSqlite(cs));
    }
}

/// <summary>
/// Proves database-per-tenant: each tenant's writes land in its own physical SQLite file, resolved
/// per scope from the current tenant. A read under tenant A never sees tenant B's data because they
/// are in separate databases (isolation by database, not just by row filter).
/// </summary>
public sealed class TenantDatabaseTests : IAsyncLifetime
{
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly string _dirRoot = Path.Combine(Path.GetTempPath(), $"slice-tenants-{Guid.NewGuid():N}");
    private IHost _host = null!;

    private string Db(string name) => $"Data Source={Path.Combine(_dirRoot, name)}.db";

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_dirRoot);

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Host"] = Db("host")
        });
        builder.Services.AddSliceModules<TenantDbTestModule>(builder.Configuration);
        builder.Services.AddTenantConnectionStrings(new Dictionary<Guid, string>
        {
            [_tenantA] = Db("tenant-a"),
            [_tenantB] = Db("tenant-b")
        });
        _host = builder.Build();
        await _host.Services.InitializeSliceModulesAsync();

        // Create the schema in each tenant database (+ host).
        await UnderTenant(null, ctx => ctx.Database.EnsureCreatedAsync());
        await UnderTenant(_tenantA, ctx => ctx.Database.EnsureCreatedAsync());
        await UnderTenant(_tenantB, ctx => ctx.Database.EnsureCreatedAsync());
    }

    public Task DisposeAsync()
    {
        _host.Dispose();
        if (Directory.Exists(_dirRoot)) Directory.Delete(_dirRoot, recursive: true);
        return Task.CompletedTask;
    }

    private async Task<T> UnderTenant<T>(Guid? tenantId, Func<TestDbContext, Task<T>> work)
    {
        var currentTenant = _host.Services.GetRequiredService<ICurrentTenant>();
        using (currentTenant.Change(tenantId))
        using (var scope = _host.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            return await work(ctx);
        }
    }

    private Task UnderTenant(Guid? tenantId, Func<TestDbContext, Task> work)
        => UnderTenant(tenantId, async ctx => { await work(ctx); return 0; });

    [Fact]
    public async Task Each_tenant_writes_to_its_own_database()
    {
        await UnderTenant(_tenantA, async ctx =>
        {
            ctx.Widgets.Add(new Widget { Id = Guid.NewGuid(), Name = "alpha" });
            await ctx.SaveChangesAsync();
        });
        await UnderTenant(_tenantB, async ctx =>
        {
            ctx.Widgets.Add(new Widget { Id = Guid.NewGuid(), Name = "beta" });
            await ctx.SaveChangesAsync();
        });

        var a = await UnderTenant(_tenantA, ctx => ctx.Widgets.Select(w => w.Name).ToListAsync());
        var b = await UnderTenant(_tenantB, ctx => ctx.Widgets.Select(w => w.Name).ToListAsync());
        var host = await UnderTenant(null, ctx => ctx.Widgets.Select(w => w.Name).ToListAsync());

        Assert.Equal(new[] { "alpha" }, a);   // tenant A's database holds only alpha
        Assert.Equal(new[] { "beta" }, b);    // tenant B's database holds only beta
        Assert.Empty(host);                    // the host/shared database has neither
    }
}
