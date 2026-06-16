using LinqToDB;
using LinqToDB.Data;
using LinqToDB.DataProvider.SQLite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Slice.Dapper;
using Slice.EntityFrameworkCore;
using Slice.LinqToDB;
using Slice.Modularity;

namespace Slice.Data.Tests;

[DependsOn(
    typeof(SliceEntityFrameworkCoreModule),
    typeof(SliceDapperModule),
    typeof(SliceLinqToDbModule))]
public sealed class DataTestModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var connectionString = context.Configuration.GetConnectionString("Test")!;
        context.Services.AddSliceDbContext<TestDbContext>(o => o.UseSqlite(connectionString));
        context.Services.AddSliceLinqToDb<TestDbContext>(SQLiteTools.GetDataProvider(ProviderName.SQLiteMS));
    }
}

/// <summary>
/// Proves Dapper and LinqToDB run on the SliceDbContext's connection + ambient transaction, so all
/// three ORMs share one unit of work: data is mutually visible inside a transaction and a rollback
/// discards everyone's writes.
/// </summary>
public sealed class SharedConnectionTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"slice-data-{Guid.NewGuid():N}.db");
    private IHost _host = null!;

    public async Task InitializeAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Test"] = $"Data Source={_dbPath}"
        });
        builder.Services.AddSliceModules<DataTestModule>(builder.Configuration);
        _host = builder.Build();
        await _host.Services.InitializeSliceModulesAsync();

        using var scope = _host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync()
    {
        _host.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task EF_Dapper_and_LinqToDB_share_the_same_connection_and_transaction()
    {
        // ── Scope 1: write through all three ORMs inside one transaction, then roll back ──
        using (var scope = _host.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var ctx = sp.GetRequiredService<TestDbContext>();
            var dapper = sp.GetRequiredService<IDapperExecutor<TestDbContext>>();
            var linq2db = sp.GetRequiredService<ISliceDataConnectionFactory<TestDbContext>>();

            await using var tx = await ctx.Database.BeginTransactionAsync();

            // EF insert (uncommitted, inside tx)
            ctx.Widgets.Add(new Widget { Id = Guid.NewGuid(), Name = "ef" });
            await ctx.SaveChangesAsync();

            // Dapper sees EF's uncommitted row → same connection + transaction
            var afterEf = await dapper.ExecuteScalarAsync<long>("select count(*) from Widgets");
            Assert.Equal(1, afterEf);

            // LinqToDB sees it too
            using (var dc = await linq2db.CreateAsync())
            {
                var l2dbCount = dc.Query<long>("select count(*) from Widgets").Single();
                Assert.Equal(1, l2dbCount);
            }

            // Dapper insert inside the same tx; EF then sees it
            await dapper.ExecuteAsync("insert into Widgets (Id, Name) values (@Id, @Name)",
                new { Id = Guid.NewGuid().ToString(), Name = "dapper" });
            var efCount = await EntityFrameworkQueryableExtensions.CountAsync(ctx.Widgets);
            Assert.Equal(2, efCount);

            await tx.RollbackAsync();   // discard everyone's writes
        }

        // ── Scope 2: fresh connection — rollback means nothing persisted ──
        using (var scope = _host.Services.CreateScope())
        {
            var dapper = scope.ServiceProvider.GetRequiredService<IDapperExecutor<TestDbContext>>();
            var count = await dapper.ExecuteScalarAsync<long>("select count(*) from Widgets");
            Assert.Equal(0, count);     // proves the shared transaction rolled back EF + Dapper writes
        }
    }
}
