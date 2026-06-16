using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Slice.Authentication;
using Slice.Authorization;
using Slice.EntityFrameworkCore;
using Slice.Modularity;

namespace Slice.Management;

/// <summary>
/// Management module: DB-backed permission grants (replacing the claims store), tenant CRUD, and
/// DB-backed setting/feature value stores. Consolidates ABP's *Management modules.
/// </summary>
[DependsOn(typeof(SliceAuthenticationModule), typeof(SliceEntityFrameworkCoreModule))]
public sealed class SliceManagementModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
        => context.Services.AddSliceConventions(typeof(SliceManagementModule).Assembly);

    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        using var scope = context.ServiceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<SliceManagementDbContext>().Database.EnsureCreatedAsync();

        // Seed: grant every declared permission to the "admin" role (DB-backed authorization).
        var definitions = sp.GetRequiredService<IPermissionDefinitionManager>();
        var grants = sp.GetRequiredService<IPermissionGrantManager>();
        foreach (var permission in definitions.GetPermissions())
            await grants.GrantAsync(PermissionProviders.Role, "admin", permission.Name);
    }
}

public static class SliceManagementRegistration
{
    /// <summary>Registers the shared management store (host picks the EF provider).</summary>
    public static IServiceCollection AddSliceManagementStore(
        this IServiceCollection services, Action<DbContextOptionsBuilder> configure)
        => services.AddSliceDbContext<SliceManagementDbContext>(configure);
}
