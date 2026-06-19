using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Slice.AspNetCore;
using Slice.AspNetCore.AppConfig;
using Slice.Authorization;
using Slice.EntityFrameworkCore;
using Slice.Features;
using Slice.Management;
using Slice.Mediator.Default;
using Slice.Modularity;

namespace Slice.AppConfig.Tests;

// ── A test app: one feature-gated permission group (Sales), one ungated (Home), a feature, a menu ──
public sealed class TestPermissions : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        context.AddGroup("Sales").RequireFeature("Sales").AddPermission("Sales.View", "View sales");
        context.AddGroup("Home").AddPermission("Home.View", "View home");
    }
}

public sealed class TestFeatures : FeatureDefinitionProvider
{
    public override void Define(IFeatureDefinitionContext context)
        => context.Add(new FeatureDefinition("Sales", defaultValue: "false", displayName: "Sales module"));
}

public sealed class TestMenu : IMenuContributor
{
    public Task ContributeAsync(MenuBuilder menu, CancellationToken ct = default)
    {
        menu.Add(new MenuItem { Name = "home", Url = "/", Order = 1 });
        menu.Add(new MenuItem { Name = "sales", Url = "/sales", Order = 2, RequiredFeature = "Sales", RequiredPermission = "Sales.View" });
        return Task.CompletedTask;
    }
}

[DependsOn(typeof(SliceAppConfigModule), typeof(SliceEntityFrameworkCoreModule))]
public sealed class AppConfigTestModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var services = context.Services;
        services.AddSliceMediator();                                                  // engine (unused here)
        services.AddSliceConventions(typeof(AppConfigTestModule).Assembly);           // permission + feature providers
        services.AddSliceMenuContributors(typeof(AppConfigTestModule).Assembly);      // menu contributor

        services.AddSliceManagementStore(o => o.UseSqlite(context.Configuration.GetConnectionString("Test")!));
        services.RemoveAll<IFeatureStore>();
        services.AddScoped<IFeatureStore, ManagementFeatureStore>();                  // DB-backed feature values
        services.AddScoped<IFeatureValueManager, FeatureValueManager>();
    }

    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        using var scope = context.ServiceProvider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SliceManagementDbContext>().Database.EnsureCreatedAsync();
    }
}

public sealed class AppConfigTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"slice-appcfg-{Guid.NewGuid():N}.db");
    private IHost _host = null!;

    public async Task InitializeAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Test"] = $"Data Source={_dbPath}",
            // ConfigurationPermissionStore grants both to everyone — so feature gating is what we isolate.
            ["Authorization:GrantedPermissions:0"] = "Home.View",
            ["Authorization:GrantedPermissions:1"] = "Sales.View",
        });
        builder.Services.AddSliceModules<AppConfigTestModule>(builder.Configuration);
        _host = builder.Build();
        await _host.Services.InitializeSliceModulesAsync();
    }

    public Task DisposeAsync()
    {
        _host.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        return Task.CompletedTask;
    }

    private async Task<AppConfigDto> GetConfigAsync()
    {
        using var scope = _host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAppConfigProvider>().GetAsync();
    }

    private async Task SetFeatureAsync(string? value)
    {
        using var scope = _host.Services.CreateScope();
        var features = scope.ServiceProvider.GetRequiredService<IFeatureValueManager>();
        if (value is null) await features.ClearAsync("Sales", "G", null);
        else await features.SetAsync("Sales", value, "G", null);
    }

    [Fact]
    public void Permission_inherits_its_group_feature()
    {
        using var scope = _host.Services.CreateScope();
        var defs = scope.ServiceProvider.GetRequiredService<IPermissionDefinitionManager>();
        Assert.Equal("Sales", defs.Find("Sales.View")!.RequiredFeature);
        Assert.Null(defs.Find("Home.View")!.RequiredFeature);
    }

    [Fact]
    public async Task Disabled_feature_hides_its_permissions_and_menu()
    {
        var cfg = await GetConfigAsync();   // Sales feature off by default

        Assert.False(cfg.Features["Sales"]);
        Assert.Contains("Home.View", cfg.GrantedPermissions);
        Assert.DoesNotContain("Sales.View", cfg.GrantedPermissions);   // granted, but feature off → hidden
        Assert.Contains(cfg.Menu, m => m.Name == "home");
        Assert.DoesNotContain(cfg.Menu, m => m.Name == "sales");
    }

    [Fact]
    public async Task Enabling_feature_reveals_its_permissions_and_menu_then_clearing_hides_again()
    {
        await SetFeatureAsync("true");
        var on = await GetConfigAsync();
        Assert.True(on.Features["Sales"]);
        Assert.Contains("Sales.View", on.GrantedPermissions);
        Assert.Contains(on.Menu, m => m.Name == "sales");

        await SetFeatureAsync(null);   // ClearAsync → back to the definition default (false)
        var off = await GetConfigAsync();
        Assert.False(off.Features["Sales"]);
        Assert.DoesNotContain("Sales.View", off.GrantedPermissions);
        Assert.DoesNotContain(off.Menu, m => m.Name == "sales");
    }
}
