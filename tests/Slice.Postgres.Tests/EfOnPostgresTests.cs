using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Slice.Core.Ambient;
using Slice.Domain.Entities;
using Slice.Domain.Events;
using Slice.EntityFrameworkCore;
using Slice.EntityFrameworkCore.PostgreSQL;
using Slice.Modularity;

namespace Slice.Postgres.Tests;

public sealed record GadgetCreated(Guid Id) : IDistributedEvent;

public sealed class Gadget : AggregateRoot<Guid>
{
    private Gadget() { }
    public Gadget(Guid id, string name) : base(id)
    {
        Name = name;
        AddDistributedEvent(new GadgetCreated(id));
    }
    public string Name { get; private set; } = string.Empty;
}

public sealed class GadgetDbContext(DbContextOptions<GadgetDbContext> options, ICurrentTenant t, IDataFilter f)
    : SliceDbContext(options, t, f)
{
    public DbSet<Gadget> Gadgets => Set<Gadget>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Gadget>(e => { e.HasKey(x => x.Id); e.Property(x => x.Name).IsRequired(); });
        base.OnModelCreating(b);
    }
}

[DependsOn(typeof(SliceEntityFrameworkCoreModule))]
public sealed class EfPgTestModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var cs = context.Configuration.GetConnectionString("Test")!;
        context.Services.AddSlicePostgres(cs);
        context.Services.AddSliceDbContext<GadgetDbContext>((sp, o) =>
            o.UseSlicePostgres(sp.GetRequiredService<NpgsqlDataSource>()));
    }
}

[Collection("postgres")]
public sealed class EfOnPostgresTests(PostgresFixture fx)
{
    [Fact]
    public async Task DbContext_runs_on_Postgres_and_writes_an_outbox_row()
    {
        // EnsureCreated is all-or-nothing per database, and the shared DB already has the adapter
        // tables — so give this EF context its own fresh database.
        var dbName = "ef_pg_" + Guid.NewGuid().ToString("N")[..12];
        await using (var admin = new NpgsqlConnection(fx.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = admin.CreateCommand();
            create.CommandText = $"CREATE DATABASE {dbName}";
            await create.ExecuteNonQueryAsync();
        }
        var efConnectionString = new NpgsqlConnectionStringBuilder(fx.ConnectionString) { Database = dbName }.ConnectionString;

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Test"] = efConnectionString
        });
        builder.Services.AddSliceModules<EfPgTestModule>(builder.Configuration);
        using var host = builder.Build();
        await host.Services.InitializeSliceModulesAsync();

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GadgetDbContext>();
        await db.Database.EnsureCreatedAsync();

        var id = Guid.CreateVersion7();
        db.Gadgets.Add(new Gadget(id, "widget"));
        await db.SaveChangesAsync();

        // entity persisted on Postgres
        Assert.NotNull(await db.Gadgets.FindAsync(id));
        // the distributed event was written to the transactional outbox in the same save
        Assert.True(await db.OutboxMessages.AnyAsync(m => m.EventType.Contains("GadgetCreated")));
    }
}
