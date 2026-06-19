# Slice.AspNetCore.AppConfig

> One endpoint a frontend can call to render itself: the current user/tenant, what they may do/see, what's enabled, and the navigation menu.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and the [docs](../../docs/).

## Overview

`GET /api/app-config` returns an `AppConfigDto` assembled per request from the framework's own managers:
current user (`ICurrentUser`) and tenant (`ICurrentTenant`), **granted permissions**, **enabled features**,
client-visible **settings**, a navigation **menu**, and the current culture. Permissions and menu items are
**feature-filtered**: a permission whose group has `RequireFeature("X")` (and any `MenuItem.RequiredFeature`)
is omitted when feature `X` is disabled for the current tenant — so a tenant without a module never sees its
permissions or nav. This is the visibility/composition layer; the `[SlicePermission]` (pipeline order 300)
and `[RequiresFeature]` (350) gates still enforce access regardless of what app-config returns.

## Dependencies

- **Slice:** `Slice.AspNetCore` (`SliceController`), `Slice.Authorization` (`IPermissionChecker`,
  `IPermissionDefinitionManager` + the group `RequireFeature`), `Slice.Features` (`IFeatureChecker`,
  `IFeatureDefinitionManager`), `Slice.Settings` (`ISettingManager`, `ISettingDefinitionManager`),
  `Slice.Modularity`.
- **Third-party:** `Microsoft.AspNetCore.App` framework reference.

## Module & registration

`SliceAppConfigModule` `[DependsOn(SliceAspNetCoreModule, SliceAuthorizationModule, SliceFeaturesModule, SliceSettingsModule)]`
registers `IAppConfigProvider` and maps the controller. Register your modules' menu items:

```csharp
services.AddSliceMenuContributors(typeof(AppModule).Assembly);   // discovers IMenuContributor implementations
```

## Key types

| Type | Kind | Description |
|---|---|---|
| `AppConfigController` | `SliceController` | `GET /api/app-config` → `AppConfigDto`. |
| `IAppConfigProvider` / `AppConfigProvider` | interface / `IScopedDependency` | Assembles the DTO (feature-filtered permissions + menu, client-visible settings). |
| `AppConfigDto` | record-like | `CurrentUser`, `CurrentTenant`, `GrantedPermissions`, `Features`, `Settings`, `Menu`, `Culture`. |
| `IMenuContributor` | interface | Per-module navigation contributor: `ContributeAsync(MenuBuilder, ct)`. |
| `MenuItem` | sealed class | `Name`, `DisplayName`, `Url`, `Icon`, `Order`, `RequiredPermission?`, `RequiredFeature?`, `Children`. |
| `MenuRegistration.AddSliceMenuContributors` | extension | Discovers + registers `IMenuContributor`s in an assembly. |

## Usage

```csharp
public sealed class CrmMenu : IMenuContributor
{
    public Task ContributeAsync(MenuBuilder menu, CancellationToken ct = default)
    {
        menu.Add(new MenuItem { Name = "leads", DisplayName = "Leads", Url = "/leads", Order = 10,
                                RequiredPermission = "Crm.Leads.View", RequiredFeature = "Crm" });
        return Task.CompletedTask;
    }
}
```

Settings appear in the DTO only when their `SettingDefinition.IsVisibleToClients` is `true` (server-only
settings are never leaked). See [cross-cutting services → feature-based module entitlement](../../docs/cross-cutting-services.md)
and [microservices](../../docs/microservices.md) (compose this in a BFF/gateway across services).
