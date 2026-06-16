# Slice.Authorization

> A pluggable permission system: declare permissions, gate commands/queries with an attribute, and enforce them in the mediator pipeline.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

This module provides permission-based authorization that is independent of any transport. Feature modules declare permissions through a `PermissionDefinitionProvider`; commands and queries are annotated with `[SlicePermission]`; and the `AuthorizationBehavior` enforces those attributes for every caller — web *and* background — by short-circuiting to a `Forbidden` result before the handler runs. The actual grant lookup is delegated to a pluggable `IPermissionStore`, with a configuration-backed default included.

## Dependencies

- **Slice:** `Slice.Core` (`Error`, DI markers), `Slice.Mediator` (`IPipelineBehavior`, `IHasPipelineOrder`, `PipelineOrder`), `Slice.Application` (`SliceApplicationModule`, `ResultFactory`), `Slice.Modularity`.
- **Third-party:** `Microsoft.Extensions.Configuration.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions`.

## Module & registration

`SliceAuthorizationModule` is a `SliceModule` with `[DependsOn(typeof(SliceApplicationModule))]`. It scans its own assembly with `AddSliceConventions(...)` (registering the checker, definition manager, default store, and discovered providers by convention) and registers the open-generic `AuthorizationBehavior<,>` as an `IPipelineBehavior<,>`.

```csharp
[DependsOn(typeof(SliceAuthorizationModule))]
public sealed class MyAppModule : SliceModule { }
```

## Key types

| Type | Kind | Description |
|---|---|---|
| `IPermissionChecker` | `interface` | `IsGrantedAsync(string permission, CancellationToken)`. |
| `PermissionChecker` | `sealed class`, `ITransientDependency` | Default checker; delegates to the `IPermissionStore`. |
| `IPermissionStore` | `interface` | Source of grants for the current caller (pluggable: config, EF, roles…). |
| `ConfigurationPermissionStore` | `sealed class`, `ISingletonDependency` | Default store; grants permissions listed under `Authorization:GrantedPermissions` plus any `GrantedByDefault`. |
| `SlicePermissionAttribute` | `sealed class : Attribute` | `[SlicePermission("...")]`; `AttributeUsage(Class, AllowMultiple=true, Inherited=true)`; exposes `Permission`. |
| `PermissionDefinition` | `sealed class` | A single permission; `Name`, `DisplayName`, `GrantedByDefault`, `Children`, `AddChild(...)`. |
| `PermissionGroupDefinition` | `sealed class` | A named group; `Name`, `DisplayName`, `Permissions`, `AddPermission(...)`. |
| `IPermissionDefinitionContext` | `interface` | `AddGroup(string name, string? displayName = null)`. |
| `PermissionDefinitionProvider` | `abstract class`, `ITransientDependency` | Implement `Define(IPermissionDefinitionContext)` to declare a module's permissions. |
| `IPermissionDefinitionManager` | `interface` | `GetGroups()`, `Find(string)`, `GetPermissions()` — aggregated/flattened view. |
| `PermissionDefinitionManager` | `sealed class`, `ISingletonDependency`, `IPermissionDefinitionContext` | Aggregates all providers (lazily) into groups + a flat lookup. |
| `AuthorizationBehavior<TRequest,TResponse>` | `sealed class`, `IPipelineBehavior`, `IHasPipelineOrder` | Enforces `[SlicePermission]`; `Order => PipelineOrder.Authorization` (300). |
| `SliceAuthorizationModule` | `sealed class : SliceModule` | Wires the above + the pipeline behavior. |

## Usage

Define a permission group in a feature module:

```csharp
public sealed class CrmPermissionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup("Crm", "CRM");
        var leads = group.AddPermission("Crm.Leads", "Leads");
        leads.AddChild("Crm.Leads.Create", "Create");
        leads.AddChild("Crm.Leads.View", "View", grantedByDefault: true);
    }
}
```

Gate a command with the attribute:

```csharp
[SlicePermission("Crm.Leads.Create")]
public sealed record CreateLead(string Name) : IRequest<Result<Guid>>;
```

When the caller lacks the permission, the behavior returns
`Error.Forbidden("Authorization:Forbidden", "Permission 'Crm.Leads.Create' is required.")` as a typed failure (or throws `SlicePipelineException` if the response type isn't a `Result`).

## Notes

- **Enforcement point:** `AuthorizationBehavior` runs at `PipelineOrder.Authorization` (300) — after logging/multi-tenancy, before validation/unit-of-work. Required permissions are read once per closed generic type via reflection (`inherit: true`, distinct), so requests with no `[SlicePermission]` pass straight through.
- **Short-circuiting:** the first missing permission produces `Error.Forbidden(...)` via `ResultFactory.FailureOrThrow<TResponse>` — no handler is invoked.
- **Default store is config-only:** `ConfigurationPermissionStore` reads `Authorization:GrantedPermissions` (string array) once at construction and adds every `GrantedByDefault` permission. It is registered as a singleton; real apps replace it with a role/user/tenant-backed `IPermissionStore` (e.g. the claims store from `Slice.Authentication` or the DB store from `Slice.Management`, both registered later so they win).
- `PermissionDefinitionManager` builds its model lazily and caches it (`Lazy<>`), so providers are evaluated once.
