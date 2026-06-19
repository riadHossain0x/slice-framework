using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Slice.AspNetCore.Mvc;
using Slice.Authorization;
using Slice.Core.Ambient;
using Slice.Core.DependencyInjection;
using Slice.Features;
using Slice.Modularity;
using Slice.Settings;

namespace Slice.AspNetCore.AppConfig;

public sealed record CurrentUserDto(bool IsAuthenticated, Guid? Id, string? UserName, string[] Roles);
public sealed record CurrentTenantDto(Guid? Id, string? Name);

/// <summary>One call for a frontend to render itself: who am I, what may I do/see, what's enabled, the menu.</summary>
public sealed class AppConfigDto
{
    public required CurrentUserDto CurrentUser { get; init; }
    public required CurrentTenantDto CurrentTenant { get; init; }
    public required IReadOnlyList<string> GrantedPermissions { get; init; }
    public required IReadOnlyDictionary<string, bool> Features { get; init; }
    public required IReadOnlyDictionary<string, string?> Settings { get; init; }
    public required IReadOnlyList<MenuItem> Menu { get; init; }
    public required string Culture { get; init; }
}

public interface IAppConfigProvider
{
    Task<AppConfigDto> GetAsync(CancellationToken ct = default);
}

/// <summary>
/// Assembles the per-user/tenant application configuration: granted permissions and menu items are
/// filtered so a disabled module (via its <c>RequiredFeature</c>) never surfaces. Only settings flagged
/// <see cref="SettingDefinition.IsVisibleToClients"/> are exposed.
/// </summary>
public sealed class AppConfigProvider(
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    IPermissionChecker permissionChecker,
    IPermissionDefinitionManager permissionDefinitions,
    IFeatureChecker featureChecker,
    IFeatureDefinitionManager featureDefinitions,
    ISettingManager settings,
    ISettingDefinitionManager settingDefinitions,
    IEnumerable<IMenuContributor> menuContributors) : IAppConfigProvider, IScopedDependency
{
    public async Task<AppConfigDto> GetAsync(CancellationToken ct = default)
    {
        // Features enabled for the current user/tenant scope.
        var features = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var f in featureDefinitions.GetAll())
            features[f.Name] = await featureChecker.IsEnabledAsync(f.Name);

        bool FeatureOk(string? feature) => feature is null || (features.TryGetValue(feature, out var on) && on);

        // Granted permissions whose owning feature (if any) is enabled.
        var granted = new List<string>();
        foreach (var p in permissionDefinitions.GetPermissions())
            if (FeatureOk(p.RequiredFeature) && await permissionChecker.IsGrantedAsync(p.Name, ct))
                granted.Add(p.Name);

        // Client-visible settings only (never leak server-only settings).
        var visibleSettings = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var s in settingDefinitions.GetAll().Where(s => s.IsVisibleToClients))
            visibleSettings[s.Name] = await settings.GetOrNullAsync(s.Name);

        // Menu: merge every contributor, then filter by permission + feature (recursively).
        var builder = new MenuBuilder();
        foreach (var contributor in menuContributors)
            await contributor.ContributeAsync(builder, ct);
        var menu = await FilterMenuAsync(builder.Items, FeatureOk, ct);

        return new AppConfigDto
        {
            CurrentUser = new CurrentUserDto(currentUser.IsAuthenticated, currentUser.Id, currentUser.UserName, currentUser.Roles),
            CurrentTenant = new CurrentTenantDto(currentTenant.Id, currentTenant.Name),
            GrantedPermissions = granted,
            Features = features,
            Settings = visibleSettings,
            Menu = menu,
            Culture = CultureInfo.CurrentUICulture.Name,
        };
    }

    private async Task<List<MenuItem>> FilterMenuAsync(IReadOnlyList<MenuItem> items, Func<string?, bool> featureOk, CancellationToken ct)
    {
        var result = new List<MenuItem>();
        foreach (var item in items.OrderBy(i => i.Order))
        {
            if (!featureOk(item.RequiredFeature)) continue;
            if (item.RequiredPermission is { } perm && !await permissionChecker.IsGrantedAsync(perm, ct)) continue;

            result.Add(new MenuItem
            {
                Name = item.Name,
                DisplayName = item.DisplayName,
                Url = item.Url,
                Icon = item.Icon,
                Order = item.Order,
                RequiredPermission = item.RequiredPermission,
                RequiredFeature = item.RequiredFeature,
                Children = await FilterMenuAsync(item.Children, featureOk, ct),
            });
        }
        return result;
    }
}

[Route("api/app-config")]
public sealed class AppConfigController(IAppConfigProvider provider) : SliceController
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await provider.GetAsync(ct));
}

/// <summary>Adds the <c>GET /api/app-config</c> endpoint + the menu/app-config services.</summary>
[DependsOn(
    typeof(SliceAspNetCoreModule),
    typeof(SliceAuthorizationModule),
    typeof(SliceFeaturesModule),
    typeof(SliceSettingsModule))]
public sealed class SliceAppConfigModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
        => context.Services.AddSliceConventions(typeof(SliceAppConfigModule).Assembly);
}
