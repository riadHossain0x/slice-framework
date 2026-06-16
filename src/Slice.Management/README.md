# Slice.Management

> DB-backed administration: permission grants, tenants, and setting/feature values, plus the admin APIs that manage them.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

This module consolidates the runtime-administration concerns that other frameworks split across several "*Management" modules. It backs permissions, tenants, settings, and features with a single EF `DbContext` and exposes admin controllers under `api/management/*`. Crucially, `PermissionManagementStore` replaces the claims-only store from `Slice.Authentication` with a database-backed `IPermissionStore`, so changing a role's or user's grants takes effect immediately — without re-issuing tokens.

## Dependencies

- **Slice:** `Slice.Core`, `Slice.Modularity`, `Slice.Application`, `Slice.Authorization` (`IPermissionStore`), `Slice.Authentication` (`SliceUser`/`SliceRole`, identity), `Slice.MultiTenancy`, `Slice.EntityFrameworkCore` (`SliceDbContext`, `AddSliceDbContext`), `Slice.AspNetCore` (`SliceController`), `Slice.Settings` (`ISettingValueProvider`), `Slice.Features` (`IFeatureStore`).
- **Third-party:** EF Core (transitively, via `Slice.EntityFrameworkCore`).

## Module & registration

`SliceManagementModule` is a `SliceModule` with `[DependsOn(typeof(SliceAuthenticationModule), typeof(SliceEntityFrameworkCoreModule))]`. It registers its assembly by convention; the host supplies the EF provider via `AddSliceManagementStore(...)`. `OnApplicationInitializationAsync` ensures the schema and seeds grants.

```csharp
[DependsOn(typeof(SliceManagementModule))]
public sealed class MyAppModule : SliceModule { }

// Host wiring:
services.AddSliceManagementStore(b => b.UseSqlite("Data Source=management.db"));
```

## Key types

| Type | Kind | Description |
|---|---|---|
| `SliceManagementDbContext` | `sealed class : SliceDbContext` | `DbSet`s: `PermissionGrants`, `Tenants`, `SettingValues`, `FeatureValues`. Tables `SlicePermissionGrants` / `SliceTenants` / `SliceSettingValues` / `SliceFeatureValues`, each with a unique index. |
| `PermissionGrant` | `sealed class` (entity) | `Id`, `Name`, `ProviderName`, `ProviderKey`. |
| `TenantRecord` | `sealed class` (entity) | `Id`, `Name` (unique). |
| `SettingValueRecord` | `sealed class` (entity) | `Id`, `Name`, `Value?`, `ProviderName` (`"G"`/`"T"`/`"U"`), `ProviderKey?`. |
| `FeatureValueRecord` | `sealed class` (entity) | `Id`, `Name`, `Value?`, `ProviderName` (`"G"`/`"T"`), `ProviderKey?`. |
| `PermissionProviders` | `static class` | `Role = "R"`, `User = "U"`. |
| `PermissionManagementStore` | `sealed class`, `IPermissionStore`, `IScopedDependency` | DB-backed grant check for current user + roles. |
| `IPermissionGrantManager` / `PermissionGrantManager` | `interface` / `sealed class`, `IScopedDependency` | `GetGrantedAsync` / `GrantAsync` / `RevokeAsync`. |
| `ITenantManager` / `TenantManager` | `interface` / `sealed class`, `IScopedDependency` | `CreateAsync(name, connectionString?)` / `GetListAsync` / `FindByNameAsync` / `FindByIdAsync` / `DeleteAsync`. |
| `ManagementTenantConnectionStore` | `sealed class`, `ITenantConnectionStore` | Cached, registry-backed per-tenant connection strings for **database-per-tenant** (`AddSliceManagementTenantConnectionStore`). |
| `ManagementSettingValueProvider` | `sealed class`, `ISettingValueProvider`, `IScopedDependency` | `Order = -10`; resolves U → T → G. |
| `ManagementFeatureStore` | `sealed class`, `IFeatureStore`, `IScopedDependency` | Resolves T → G → `Features:{name}` config. |
| `PermissionManagementController` | `sealed class : SliceController` | `[Authorize]`, route `api/management/permissions`. |
| `TenantManagementController` | `sealed class : SliceController` | `[Authorize]`, route `api/management/tenants`. |
| `IdentityManagementController` | `sealed class : SliceController` | `[Authorize]`, route `api/management/identity`. |
| `SliceManagementModule` | `sealed class : SliceModule` | Wires the module and seeds admin grants. |
| `SliceManagementRegistration` | `static class` | `AddSliceManagementStore(this IServiceCollection, Action<DbContextOptionsBuilder>)`. |

## Usage

Grant and revoke permissions (DB-backed):

```http
POST /api/management/permissions/grant
{ "providerName": "R", "providerKey": "manager", "permission": "Crm.Leads.Create" }

POST /api/management/permissions/revoke
{ "providerName": "U", "providerKey": "<userId>", "permission": "Crm.Leads.Create" }

GET  /api/management/permissions?providerName=R&providerKey=manager
```

Create a tenant, role, or user:

```http
POST /api/management/tenants        { "name": "acme" }
POST /api/management/identity/roles { "name": "manager" }
POST /api/management/identity/users { "email": "u@acme", "password": "P@ss!", "role": "manager" }
```

Programmatically:

```csharp
await grants.GrantAsync(PermissionProviders.Role, "manager", "Crm.Leads.Create", ct);
var tenant = await tenants.CreateAsync("acme", connectionString: "Data Source=acme.db", ct);   // db-per-tenant
```

## Notes

- **Immediate permission changes:** `PermissionManagementStore` grants a permission when there is a matching `PermissionGrant` for the current user (`ProviderName == "U"`, `ProviderKey == user id`) or for any of the user's roles (`ProviderName == "R"`, `ProviderKey == role name`). Because the check hits the database per request, grant/revoke changes apply without re-issuing tokens — unlike the claims store it replaces (registered earlier, so this scoped store wins).
- **Admin seeding:** `OnApplicationInitializationAsync` calls `EnsureCreatedAsync`, then grants **every declared permission** to the `"admin"` role via `IPermissionGrantManager`. `GrantAsync` is idempotent (skips if the grant already exists).
- **Provider codes:** permissions use `"R"`/`"U"`; settings use `"G"`/`"T"`/`"U"`; features use `"G"`/`"T"`.
- **Setting precedence:** `ManagementSettingValueProvider` has `Order = -10` (highest priority, above global-override/config/default providers) and resolves user → tenant → global.
- **Feature precedence:** `ManagementFeatureStore` resolves tenant → global → `configuration["Features:{name}"]`.
- All managers and stores are scoped (`IScopedDependency`); the `DbContext` derives from `SliceDbContext` so multi-tenancy data filters and auditing apply. Management controllers are gated with `[Authorize]` (authentication required; add `[SlicePermission]` on the underlying operations to gate them further).
