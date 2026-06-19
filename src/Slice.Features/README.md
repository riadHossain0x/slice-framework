# Slice.Features

> Define feature flags, check whether they are enabled, and gate mediator requests with `[RequiresFeature]`.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

Slice.Features is a lightweight feature-flag system. Modules declare `FeatureDefinition`s through `FeatureDefinitionProvider`s; values are read through an `IFeatureStore` (config-backed by default), falling back to a definition's declared default. `IFeatureChecker` resolves a feature's value and whether it is enabled. A mediator pipeline behavior, `RequiresFeatureBehavior`, enforces the `[RequiresFeature]` attribute on requests by short-circuiting to a forbidden result when a required feature is disabled.

## Dependencies

- **Slice:** `Slice.Core` (DI markers), `Slice.Modularity`, `Slice.Application` (`SliceApplicationModule`, `ResultFactory`), `Slice.Mediator` (`IPipelineBehavior`, `IHasPipelineOrder`, `PipelineOrder`)
- **Third-party:** `Microsoft.Extensions.Configuration.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions`

## Module & registration

```csharp
[DependsOn(typeof(SliceApplicationModule))]
public sealed class SliceFeaturesModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddSliceConventions(typeof(SliceFeaturesModule).Assembly);
        context.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(RequiresFeatureBehavior<,>));
    }
}
```

`AddSliceConventions` registers the definition manager, `ConfigurationFeatureStore`, and `FeatureChecker`. The module also explicitly registers the open-generic `RequiresFeatureBehavior<,>` into the mediator pipeline. `FeatureDefinitionProvider`s in feature modules are discovered by convention (they are `ITransientDependency`).

## Key types

| Type | Kind | Description |
|---|---|---|
| `FeatureDefinition` | sealed class | Declares a feature: `Name`, `DefaultValue` (`string?`, defaults to `"false"`), `DisplayName` (defaults to `Name`). |
| `IFeatureDefinitionContext` | interface | Passed to providers; `void Add(FeatureDefinition definition)`. |
| `FeatureDefinitionProvider` | abstract class (`ITransientDependency`) | Override `void Define(IFeatureDefinitionContext context)` to declare a module's features. |
| `IFeatureDefinitionManager` / `FeatureDefinitionManager` | interface / sealed (`ISingletonDependency`) | Aggregates definitions lazily. `FeatureDefinition? GetOrNull(string name)`, `IReadOnlyList<FeatureDefinition> GetAll()`. |
| `IFeatureStore` | interface | Source of feature values: `Task<string?> GetOrNullAsync(string name)`. |
| `ConfigurationFeatureStore` | sealed (`ISingletonDependency`) | Default store; reads `Features:{name}` from `IConfiguration`. |
| `IFeatureChecker` / `FeatureChecker` | interface / sealed (`ITransientDependency`) | `Task<string?> GetOrNullAsync(string name)` (store value, else definition default), `Task<bool> IsEnabledAsync(string name)` (true when value equals `"true"`, case-insensitive). |
| `RequiresFeatureAttribute` | sealed `Attribute` | `[RequiresFeature("...")]` on a request class; `AllowMultiple = true`, `Inherited = true`. Exposes `Feature`. |
| `RequiresFeatureBehavior<TRequest, TResponse>` | sealed pipeline behavior (`IHasPipelineOrder`) | Enforces the union of the request's `RequiresFeature` attributes and any module-level requirement for the request's assembly; short-circuits to `Error.Forbidden("Features:Disabled", ...)` if any required feature is disabled. `Order => PipelineOrder.FeatureCheck`. |
| `IModuleFeatureRegistry` / `ModuleFeatureRegistry` | interface / `ISingletonDependency` | The set of features required for each module assembly (built from registered `ModuleFeatureRequirement`s). |
| `ModuleFeatureGating.RequireFeature<TModule>` / `RequireFeatureForAssembly` | extension | One-line module gate: `services.RequireFeature<SalesModule>("Sales")` makes every request in that module's assembly require the feature (composes with per-slice `[RequiresFeature]`). |

## Usage

Define features in a feature module:

```csharp
public sealed class CatalogFeatureDefinitionProvider : FeatureDefinitionProvider
{
    public override void Define(IFeatureDefinitionContext context)
    {
        context.Add(new FeatureDefinition("Catalog.Recommendations")); // defaults to "false"
        context.Add(new FeatureDefinition("Catalog.Export", defaultValue: "true",
            displayName: "CSV export"));
    }
}
```

Check a feature imperatively:

```csharp
public sealed class RecommendationService(IFeatureChecker features)
{
    public async Task RunAsync()
    {
        if (await features.IsEnabledAsync("Catalog.Recommendations"))
        {
            // ...
        }
    }
}
```

Gate a command/query declaratively — the behavior runs automatically in the pipeline:

```csharp
[RequiresFeature("Catalog.Export")]
public sealed record ExportCatalogCommand(Guid CatalogId) : IRequest<Result>;
```

If `Catalog.Export` is disabled, the handler never runs and the request resolves to `Error.Forbidden("Features:Disabled", "Feature 'Catalog.Export' is not enabled.")`.

Enable features via configuration, key prefix `Features:`:

```json
{
  "Features": {
    "Catalog.Recommendations": "true",
    "Catalog.Export": "false"
  }
}
```

## Notes

- **Resolution order.** `IFeatureChecker` returns the `IFeatureStore` value if present, otherwise the definition's `DefaultValue` (which itself defaults to `"false"`). `IsEnabledAsync` is true only when the resolved value equals `"true"` (case-insensitive).
- **Undefined features throw.** `FeatureChecker.GetOrNullAsync` throws `InvalidOperationException` for a name with no `FeatureDefinition`.
- **Pipeline position.** `RequiresFeatureBehavior` runs at `PipelineOrder.FeatureCheck` (350). Required feature names are read once per closed generic type via reflection (`inherit: true`) and de-duplicated.
- **Lifetimes.** `FeatureDefinitionManager` and `ConfigurationFeatureStore` are singletons; `FeatureChecker` and the behavior are transient.
- The default `ConfigurationFeatureStore` is config-only; production apps typically supply their own `IFeatureStore` (e.g. tenant/edition-aware) implementing `Task<string?> GetOrNullAsync(string name)`.
