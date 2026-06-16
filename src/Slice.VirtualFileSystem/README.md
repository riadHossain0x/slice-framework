# Slice.VirtualFileSystem

> A composite, read-only virtual file system over embedded resources and physical folders — for localization JSON, email templates, seed data, and the like.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

`Slice.VirtualFileSystem` lets a Slice app read files without caring whether they ship as embedded assembly resources or live on disk. You declare sources — embedded resources from any assembly and/or physical roots — and they are layered into a single `CompositeFileProvider` behind the `IVirtualFileProvider` abstraction. The provider exposes `GetFileInfo` for `IFileInfo` metadata and a convenience `ReadAsStringAsync` for reading text content.

## Dependencies

- **Slice:** `Slice.Core`, `Slice.Modularity`
- **Third-party:** `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.FileProviders.Abstractions`, `Microsoft.Extensions.FileProviders.Composite`, `Microsoft.Extensions.FileProviders.Embedded`, `Microsoft.Extensions.FileProviders.Physical`

## Module & registration

Add `SliceVirtualFileSystemModule` to your module graph (it registers `VirtualFileProvider` via `AddSliceConventions`), and configure the sources with `ConfigureVirtualFileSystem`.

```csharp
[DependsOn(typeof(SliceVirtualFileSystemModule))]
public sealed class MyFeatureModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.ConfigureVirtualFileSystem(options =>
        {
            options.AddEmbedded<MyFeatureModule>();          // embedded resources of this assembly
            options.AddPhysical("/var/app/templates");        // optional physical root
        });
    }
}
```

> Note: enabling embedded resources requires the assembly to embed them — e.g. `<EmbeddedResource Include="Resources/**" />` and `<GenerateEmbeddedFilesManifest>true</GenerateEmbeddedFilesManifest>` in the consuming `.csproj`.

## Key types

| Type | Kind | Description |
|---|---|---|
| `IVirtualFileProvider` | Interface | The VFS abstraction: `IFileInfo GetFileInfo(string path)` and `Task<string?> ReadAsStringAsync(string path)`. |
| `VirtualFileSystemOptions` | Class (`sealed`) | Source builder. `AddEmbedded<TMarker>(string? baseNamespace = null)` maps `TMarker`'s assembly's embedded resources (base namespace defaults to the assembly name); `AddPhysical(string root)` maps a folder. Both return the options for chaining. |
| `VirtualFileProvider` | Class (`sealed`, `IVirtualFileProvider`, `ISingletonDependency`) | Composes the configured providers into a `CompositeFileProvider`; `ReadAsStringAsync` returns `null` when the file does not exist. |
| `SliceVirtualFileSystemModule` | `SliceModule` | Registers `VirtualFileProvider` via `AddSliceConventions`. |
| `VirtualFileSystemRegistration.ConfigureVirtualFileSystem(this IServiceCollection, Action<VirtualFileSystemOptions>)` | Extension method | Builds `VirtualFileSystemOptions`, applies your configuration, and registers it as a singleton. |

## Usage

Register sources, then read files through the injected provider:

```csharp
public sealed class EmailTemplateRenderer(IVirtualFileProvider files)
{
    public async Task<string> RenderWelcomeAsync()
    {
        var template = await files.ReadAsStringAsync("Templates/welcome.html");
        if (template is null)
            throw new FileNotFoundException("welcome.html not found in the virtual file system.");
        return template; // ... substitute placeholders, etc.
    }
}
```

Or inspect file metadata directly:

```csharp
var info = files.GetFileInfo("Localization/en.json");
if (info.Exists)
{
    await using var stream = info.CreateReadStream();
    // ...
}
```

## Notes

- `VirtualFileProvider` is a **singleton** (`ISingletonDependency`); the underlying `CompositeFileProvider` is built once from the configured `VirtualFileSystemOptions`.
- Sources are **layered in registration order**: the first provider that resolves a given path wins, so add higher-priority sources first.
- `AddEmbedded<TMarker>` uses `TMarker`'s assembly; the optional `baseNamespace` controls how embedded-resource names map to paths and defaults to the assembly's simple name.
- The file system is **read-only** — there is no write/delete surface.
- `ReadAsStringAsync` returns `null` (not an exception) for a missing file; check the result before using it.
