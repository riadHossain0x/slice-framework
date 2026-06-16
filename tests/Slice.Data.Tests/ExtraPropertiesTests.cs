using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Slice.Domain;
using Slice.EntityFrameworkCore.ExtraProperties;
using Slice.Modularity;

namespace Slice.Data.Tests;

/// <summary>
/// ExtraProperties: a JSON column on every aggregate, with typed get/set, change tracking, and a
/// server-side equality filter (SQLite <c>json_extract</c> translation).
/// </summary>
public sealed class ExtraPropertiesTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"slice-xprops-{Guid.NewGuid():N}.db");
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

    private TestDbContext NewContext(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<TestDbContext>();

    [Fact]
    public async Task Set_then_get_round_trips_with_typed_conversion()
    {
        var id = Guid.NewGuid();
        using (var scope = _host.Services.CreateScope())
        {
            var db = NewContext(scope);
            var gizmo = new Gizmo(id, "alpha");
            gizmo.SetProperty("region", "EU").SetProperty("rank", 3);
            db.Gizmos.Add(gizmo);
            await db.SaveChangesAsync();
        }

        using (var scope = _host.Services.CreateScope())   // fresh context → values come back via JSON
        {
            var gizmo = await NewContext(scope).Gizmos.SingleAsync(g => g.Id == id);
            Assert.Equal("EU", gizmo.GetProperty<string>("region"));
            Assert.Equal(3, gizmo.GetProperty<int>("rank"));         // typed conversion from JSON
            Assert.True(gizmo.HasProperty("region"));
            Assert.Null(gizmo.GetProperty<string>("missing"));
        }
    }

    [Fact]
    public async Task Mutating_an_extra_property_is_detected_and_persisted()
    {
        var id = Guid.NewGuid();
        using (var scope = _host.Services.CreateScope())
        {
            var db = NewContext(scope);
            db.Gizmos.Add(new Gizmo(id, "beta").SetProperty("region", "EU"));
            await db.SaveChangesAsync();
        }
        using (var scope = _host.Services.CreateScope())
        {
            var db = NewContext(scope);
            var gizmo = await db.Gizmos.SingleAsync(g => g.Id == id);
            gizmo.SetProperty("region", "APAC");               // mutate the dictionary
            await db.SaveChangesAsync();                        // ValueComparer must detect the change
        }
        using (var scope = _host.Services.CreateScope())
        {
            var gizmo = await NewContext(scope).Gizmos.SingleAsync(g => g.Id == id);
            Assert.Equal("APAC", gizmo.GetProperty<string>("region"));
        }
    }

    [Fact]
    public async Task WhereExtraProperty_filters_server_side()
    {
        using (var scope = _host.Services.CreateScope())
        {
            var db = NewContext(scope);
            db.Gizmos.Add(new Gizmo(Guid.NewGuid(), "eu-1").SetProperty("region", "EU"));
            db.Gizmos.Add(new Gizmo(Guid.NewGuid(), "eu-2").SetProperty("region", "EU"));
            db.Gizmos.Add(new Gizmo(Guid.NewGuid(), "us-1").SetProperty("region", "US"));
            await db.SaveChangesAsync();
        }

        using (var scope = _host.Services.CreateScope())
        {
            var db = NewContext(scope);
            var eu = await db.Gizmos.WhereExtraProperty("region", "EU").ToListAsync();
            var us = await db.Gizmos.WhereExtraProperty("region", "US").ToListAsync();

            Assert.Equal(2, eu.Count);
            Assert.All(eu, g => Assert.Equal("EU", g.GetProperty<string>("region")));
            Assert.Single(us);
            Assert.Equal("us-1", us[0].Name);
        }
    }
}
