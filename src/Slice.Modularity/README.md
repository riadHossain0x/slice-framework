# Slice.Modularity

> The module system: `SliceModule` lifecycle, `[DependsOn]` topological loading, and convention-based DI registration driven by the Core marker interfaces.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

`Slice.Modularity` wires an application together from independent modules. Each bounded context or framework feature is a `SliceModule` that declares its dependencies with `[DependsOn(...)]`; `AddSliceModules<TRoot>()` walks the dependency graph, topologically sorts it, runs each module's service-configuration phases in dependency-first order, and registers a `SliceModuleManager` for later async initialization/shutdown. It also ships `AddSliceConventions(assembly)`, the convention registrar that maps the `Slice.Core` DI marker interfaces (`ITransientDependency`/`IScopedDependency`/`ISingletonDependency`) to service lifetimes. It depends only on `Slice.Core` and the Microsoft.Extensions abstractions.

## Dependencies

- **Slice:** `Slice.Core` (project reference)
- **Third-party:** `Microsoft.Extensions.Configuration.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`

## Module & registration

This package defines the `SliceModule` base class itself rather than a module instance. A host bootstraps a root module, configures all transitively-depended modules, builds the container, then initializes:

```csharp
using Slice.Modularity;

var builder = WebApplication.CreateBuilder(args);

// Loads CrmModule + everything it [DependsOn], in topological order,
// and runs Pre/Configure/Post service-configuration phases.
builder.Services.AddSliceModules<CrmModule>(builder.Configuration);

var app = builder.Build();

// Runs each module's OnApplicationInitializationAsync (dependency-first).
await app.Services.InitializeSliceModulesAsync();

await app.RunAsync();

// On shutdown: runs OnApplicationShutdownAsync in reverse order.
await app.Services.ShutdownSliceModulesAsync();
```

A module declares dependencies and self-registers its feature services:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Slice.Modularity;

[DependsOn(typeof(SliceEntityFrameworkCoreModule), typeof(SliceAuthorizationModule))]
public sealed class CrmModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // Scan this assembly for marker-bearing classes (handlers, providers, repos…)
        context.Services.AddSliceConventions(typeof(CrmModule).Assembly);
    }

    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        using var scope = context.ServiceProvider.CreateScope();
        // e.g. ensure database created, seed data…
    }
}
```

## Key types

| Type | Kind | Description |
|---|---|---|
| `SliceModule` | abstract class | Module base with virtual lifecycle hooks: `PreConfigureServices`, `ConfigureServices`, `PostConfigureServices`, `OnApplicationInitializationAsync`, `OnApplicationShutdownAsync`. |
| `DependsOnAttribute` | sealed attribute | `[DependsOn(params Type[])]` on a module class; exposes `DependedModuleTypes`. Class-only, single-use, not inherited. |
| `ServiceConfigurationContext` | sealed class | Passed to the configure phases: `Services`, `Configuration`, and a cross-module `Items` bag. |
| `ApplicationInitializationContext` | sealed class | Passed to init/shutdown: `ServiceProvider`. |
| `ModuleDescriptor` | sealed record | `(Type Type, SliceModule Instance)`. |
| `ModuleLoader` | static class | `LoadModules(Type rootModuleType)` — resolves the module set and returns it in dependency-first order. |
| `SliceModuleCycleException` | sealed exception | Thrown when the `[DependsOn]` graph contains a cycle. |
| `SliceModuleManager` | sealed class | Holds the ordered `Modules`; `InitializeAsync` / `ShutdownAsync`. Registered as a singleton. |
| `SliceApplicationBuilder` | static class | `AddSliceModules<TRootModule>(IServiceCollection, IConfiguration)`. |
| `ServiceProviderExtensions` | static class | `InitializeSliceModulesAsync()` / `ShutdownSliceModulesAsync()` on `IServiceProvider`. |
| `ConventionalRegistrar` | static class | `AddSliceConventions(IServiceCollection, Assembly)` — marker → lifetime auto-registration. |

## Usage

The convention registrar in detail — a marker-bearing class is registered against itself, its interfaces, and any abstract base class that also carries a marker:

```csharp
using Slice.Core.DependencyInjection;

public interface ILeadRepository { /* … */ }

// Registered as Scoped, exposed as ILeadRepository and as itself.
public sealed class EfLeadRepository : ILeadRepository, IScopedDependency { /* … */ }

// Abstract provider base carrying a marker:
public abstract class PermissionDefinitionProvider : ISingletonDependency { /* … */ }

// Both concrete providers below are also resolvable via IEnumerable<PermissionDefinitionProvider>.
public sealed class CrmPermissions   : PermissionDefinitionProvider { /* … */ }
public sealed class AdminPermissions : PermissionDefinitionProvider { /* … */ }

// In a module:
services.AddSliceConventions(typeof(CrmModule).Assembly);
```

Cross-module handshake via the `Items` bag during configuration:

```csharp
public override void PreConfigureServices(ServiceConfigurationContext context)
    => context.Items["Crm:Options"] = new CrmOptions();

public override void ConfigureServices(ServiceConfigurationContext context)
{
    if (context.Items.TryGetValue("Crm:Options", out var opt) && opt is CrmOptions options)
        context.Services.AddSingleton(options);
}
```

## Notes

- **Marker → lifetime mapping** (in `ConventionalRegistrar.ResolveLifetime`): `ISingletonDependency` → `Singleton`, `IScopedDependency` → `Scoped`, `ITransientDependency` → `Transient`. Checked in that order; classes implementing no marker are skipped. Only concrete classes (`IsClass && !IsAbstract`) are scanned.
- **What gets registered:** the concrete type against itself (guarded so it isn't added twice), then each directly-implemented interface *except the three markers themselves* (via a factory delegating to the self-registration so all share one instance per scope), then any abstract base class up the chain that itself carries a marker — enabling `IEnumerable<TBase>` to resolve every provider subtype (e.g. permission-definition providers).
- **Loading order:** `ModuleLoader` collects the transitive closure of `[DependsOn]` (validating each type is a `SliceModule`), then topologically sorts with Kahn's algorithm. The ready-set is a `SortedSet` ordered by `Type.FullName`, so sibling order is deterministic. A leftover-after-sort condition throws `SliceModuleCycleException` naming the cyclic modules.
- **Phase order:** `AddSliceModules<TRoot>` runs all modules' `PreConfigureServices`, then all `ConfigureServices`, then all `PostConfigureServices` — each pass dependency-first across the full module list (not per-module). It then registers the `SliceModuleManager` as a singleton.
- **Init / shutdown:** `InitializeSliceModulesAsync()` runs `OnApplicationInitializationAsync` dependency-first; `ShutdownSliceModulesAsync()` runs `OnApplicationShutdownAsync` in reverse (dependents first). Both resolve `SliceModuleManager` from DI. All `SliceModule` lifecycle hooks have no-op defaults, so override only what you need.
- **Module instantiation:** modules are created via `Activator.CreateInstance`, so a `SliceModule` needs a public parameterless constructor.
