# Slice.Settings

> Define named application settings and resolve their effective values through an ordered provider chain.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

Slice.Settings provides a small, layered settings system. Modules declare `SettingDefinition`s through `SettingDefinitionProvider`s, and at read time the effective value is resolved by walking an ordered chain of `ISettingValueProvider`s (runtime global override → configuration → declared default). Values are stored and returned as strings, with a typed `GetAsync<T>` helper for `IParsable<T>` conversions. A runtime in-memory store backs global overrides set via `SetGlobalAsync`.

## Dependencies

- **Slice:** `Slice.Core` (DI markers), `Slice.Modularity`, `Slice.Application` (`SliceApplicationModule`)
- **Third-party:** `Microsoft.Extensions.Configuration.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions`

## Module & registration

```csharp
[DependsOn(typeof(SliceApplicationModule))]
public sealed class SliceSettingsModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
        => context.Services.AddSliceConventions(typeof(SliceSettingsModule).Assembly);
}
```

`AddSliceConventions` discovers and registers the convention-based services in the assembly: the definition manager, the three built-in value providers, the global store, and the setting manager. `SettingDefinitionProvider`s declared in feature modules are discovered the same way (they are `ITransientDependency`).

## Key types

| Type | Kind | Description |
|---|---|---|
| `SettingDefinition` | sealed class | Declares a setting: `Name`, `DefaultValue` (`string?`), `DisplayName` (defaults to `Name`), `IsVisibleToClients` (`bool`, default `false`). |
| `ISettingDefinitionContext` | interface | Passed to providers; `void Add(SettingDefinition definition)`. |
| `SettingDefinitionProvider` | abstract class (`ITransientDependency`) | Override `void Define(ISettingDefinitionContext context)` to declare a module's settings. |
| `ISettingDefinitionManager` / `SettingDefinitionManager` | interface / sealed (`ISingletonDependency`) | Aggregates all providers' definitions (lazily, once). `SettingDefinition? GetOrNull(string name)`, `IReadOnlyList<SettingDefinition> GetAll()`. |
| `ISettingValueProvider` | interface | A value source. `int Order { get; }` (lower = higher priority), `Task<string?> GetOrNullAsync(SettingDefinition setting)`. |
| `GlobalSettingValueProvider` | sealed (`ITransientDependency`) | `Order => 0`. Reads runtime global overrides from `IGlobalSettingStore`. |
| `ConfigurationSettingValueProvider` | sealed (`ITransientDependency`) | `Order => 10`. Reads `Settings:{name}` from `IConfiguration`. |
| `DefaultValueSettingValueProvider` | sealed (`ITransientDependency`) | `Order => 100`. Returns the definition's `DefaultValue`. |
| `IGlobalSettingStore` / `InMemoryGlobalSettingStore` | interface / sealed (`ISingletonDependency`) | In-memory override store: `string? GetOrNull(string)`, `void Set(string, string?)`. |
| `ISettingManager` / `SettingManager` | interface / sealed (`ITransientDependency`) | Resolves effective values across the ordered provider chain and sets global overrides. |

## Usage

Define settings in a feature module:

```csharp
public sealed class BillingSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        context.Add(new SettingDefinition(
            name: "Billing.Currency",
            defaultValue: "USD",
            displayName: "Billing currency",
            isVisibleToClients: true));

        context.Add(new SettingDefinition("Billing.RetryCount", defaultValue: "3"));
    }
}
```

Read settings:

```csharp
public sealed class CheckoutService(ISettingManager settings)
{
    public async Task RunAsync()
    {
        // Raw string value resolved through the provider chain.
        string? currency = await settings.GetOrNullAsync("Billing.Currency");

        // Typed read; falls back to the supplied default if missing/unparsable.
        int retries = await settings.GetAsync("Billing.RetryCount", defaultValue: 3);

        // All settings with their effective values.
        IReadOnlyDictionary<string, string?> all = await settings.GetAllAsync();
    }
}
```

Set a runtime global override (highest priority):

```csharp
await settings.SetGlobalAsync("Billing.Currency", "EUR");
```

Override via configuration (`appsettings.json` / env), key prefix `Settings:`:

```json
{
  "Settings": {
    "Billing.Currency": "GBP",
    "Billing.RetryCount": "5"
  }
}
```

## Notes

- **Provider precedence** is by ascending `Order`, resolved as first-non-null-wins: `GlobalSettingValueProvider` (0) → `ConfigurationSettingValueProvider` (10) → `DefaultValueSettingValueProvider` (100). The manager sorts providers by `Order` once in its constructor.
- **Undefined settings throw.** `GetOrNullAsync` and `SetGlobalAsync` throw `InvalidOperationException` for a name with no `SettingDefinition`.
- **Typed read.** `GetAsync<T>` requires `T : IParsable<T>` and uses `T.TryParse` with `null` format provider; on parse failure it returns the supplied `defaultValue`.
- **Lifetimes.** `SettingDefinitionManager` and `InMemoryGlobalSettingStore` are singletons (definitions are aggregated lazily, exactly once; overrides persist for process lifetime). `SettingManager` and the value providers are transient.
- Definition names are matched with `StringComparer.Ordinal`; a later `Add` of the same name replaces the earlier one.
