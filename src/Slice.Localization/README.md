# Slice.Localization

> Merge per-culture string contributors into a culture-aware lookup and resolve localized strings for the current UI culture.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

Slice.Localization provides a minimal, `IStringLocalizer`-style abstraction. Feature modules register `ILocalizationContributor`s, each supplying a dictionary of strings for one culture. These are merged per culture into `SliceLocalizer`, which resolves a key against `CultureInfo.CurrentUICulture`, then the culture's two-letter parent, then the configured default culture, and finally the key itself. Web hosts call `app.UseSliceLocalization()` to wire ASP.NET Core request localization from the registered contributors.

## Dependencies

- **Slice:** `Slice.Core` (DI markers), `Slice.Modularity`
- **Third-party:** `Microsoft.AspNetCore.App` (framework reference, for `RequestLocalization`), `Microsoft.Extensions.DependencyInjection.Abstractions`

## Module & registration

```csharp
public sealed class SliceLocalizationModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddSingleton(new LocalizationOptions());
        context.Services.AddSliceConventions(typeof(SliceLocalizationModule).Assembly);
    }
}
```

The module registers `LocalizationOptions` (default culture `"en"`) and discovers `SliceLocalizer` plus all `ILocalizationContributor`s by convention. In a web host, enable request localization in the pipeline:

```csharp
app.UseSliceLocalization();
```

## Key types

| Type | Kind | Description |
|---|---|---|
| `LocalizationOptions` | sealed class | `string DefaultCulture { get; set; }` (default `"en"`). |
| `ILocalizationContributor` | interface | Contributes strings for one culture: `string Culture { get; }`, `IReadOnlyDictionary<string, string> GetStrings()`. Multiple contributors per culture are merged. |
| `ISliceLocalizer` | interface | Resolves a localized string: indexer `string this[string key]`, and `string Format(string key, params object[] args)`. |
| `SliceLocalizer` | sealed (`ISingletonDependency`) | Merges all contributors into a per-culture lookup; resolves with current → parent → default → key fallback. |
| `LocalizationApplicationBuilderExtensions` | static class | `IApplicationBuilder UseSliceLocalization(this IApplicationBuilder app)` — configures `RequestLocalizationOptions` from contributors + `LocalizationOptions`. |

## Usage

Register a resource (contributor) per culture in a feature module:

```csharp
public sealed class EnglishStrings : ILocalizationContributor
{
    public string Culture => "en";
    public IReadOnlyDictionary<string, string> GetStrings() => new Dictionary<string, string>
    {
        ["Greeting"] = "Hello",
        ["ItemCount"] = "You have {0} item(s)",
    };
}

public sealed class SpanishStrings : ILocalizationContributor
{
    public string Culture => "es";
    public IReadOnlyDictionary<string, string> GetStrings() => new Dictionary<string, string>
    {
        ["Greeting"] = "Hola",
        ["ItemCount"] = "Tienes {0} artículo(s)",
    };
}
```

Resolve strings:

```csharp
public sealed class WelcomeService(ISliceLocalizer l)
{
    public string Greet() => l["Greeting"];               // resolves for CurrentUICulture
    public string Count(int n) => l.Format("ItemCount", n); // string.Format with args
}
```

Enable request culture resolution in a web host:

```csharp
var app = builder.Build();
app.UseSliceLocalization(); // Accept-Language by default; supported cultures = registered contributors + default
```

## Notes

- **Fallback chain.** A key is looked up against `CurrentUICulture.Name`, then `CurrentUICulture.TwoLetterISOLanguageName` (e.g. `es-MX` → `es`), then `LocalizationOptions.DefaultCulture`. If none match, the key itself is returned.
- **Merging.** Contributors are grouped by `Culture` (case-insensitive). Within a culture, duplicate keys resolve to the last contributor's value (keys compared ordinal).
- **Lifetime.** `SliceLocalizer` is a singleton — the merged lookup is built once at construction, so contributors must be registered before it is resolved.
- **Supported cultures.** `UseSliceLocalization` derives `SupportedCultures` / `SupportedUICultures` from the distinct contributor cultures plus the default; the default culture is set via `SetDefaultCulture`. The package targets `Microsoft.AspNetCore.App`, so this extension is for web hosts.
