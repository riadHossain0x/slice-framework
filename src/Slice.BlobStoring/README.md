# Slice.BlobStoring

> Backend-agnostic blob storage abstraction with typed containers and a default file-system provider.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

This is the core blob-storing abstraction for Slice. Application code talks to a typed `IBlobContainer<TContainer>` and never touches a storage SDK directly; an `IBlobProvider` does the actual I/O. A logical container name is resolved from a marker type via `IBlobContainerNameResolver`, so swapping the backend (FileSystem, Azure, AWS S3, MinIO) is a registration concern only. Out of the box this module wires up `FileSystemBlobProvider`, which stores blobs on local disk.

## Dependencies

- **Slice:** `Slice.Core`, `Slice.Modularity`, `Slice.Application`
- **Third-party:** `Microsoft.Extensions.Configuration.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions`

## Module & registration

`SliceBlobStoringModule` is a `SliceModule` with `[DependsOn(typeof(SliceApplicationModule))]`. It registers the name resolver and `FileSystemBlobProvider` via `AddSliceConventions`, binds `FileSystemBlobOptions` (reading `BlobStoring:FileSystem:BasePath` from configuration if present), and registers the open generic `IBlobContainer<>` → `BlobContainer<>` as transient.

```csharp
[DependsOn(typeof(SliceBlobStoringModule))]
public sealed class MyAppModule : SliceModule;
```

Configuration (optional) to override the disk root:

```json
{ "BlobStoring": { "FileSystem": { "BasePath": "/var/data/blobs" } } }
```

## Key types

| Type | Kind | Description |
|---|---|---|
| `IBlobContainer` | interface | Container-scoped API: `SaveAsync(string name, Stream stream, bool overrideExisting = false, CancellationToken ct = default)`, `GetOrNullAsync(string name, CancellationToken ct = default)`, `ExistsAsync(string name, CancellationToken ct = default)`, `DeleteAsync(string name, CancellationToken ct = default)`. |
| `IBlobContainer<TContainer>` | interface | Marker-typed container; inherits `IBlobContainer`. Inject this. |
| `BlobContainer<TContainer>` | class | Default `IBlobContainer<TContainer>` impl. Resolves its container name once via the resolver and delegates each call to `IBlobProvider`. |
| `IBlobProvider` | interface | Storage backend keyed by `(container, blob)`: `SaveAsync(string container, string blob, Stream stream, bool overrideExisting, CancellationToken ct)`, `GetOrNullAsync(string container, string blob, CancellationToken ct)`, `ExistsAsync(...)`, `DeleteAsync(...)`. |
| `IBlobContainerNameResolver` | interface | `string Resolve(Type containerType)`. |
| `BlobContainerNameResolver` | class | `ISingletonDependency`. Returns `[BlobContainerName]` value if present; otherwise the marker class name with a trailing `"Container"` stripped, lower-cased (`InvariantCulture`). |
| `BlobContainerNameAttribute` | attribute | `[BlobContainerName("name")]` on a marker class overrides the derived container name. Class-targeted, not inherited. |
| `FileSystemBlobProvider` | class | `IBlobProvider`, `ISingletonDependency`. Stores blobs at `{BasePath}/{container}/{blob}` on disk; guards against path traversal. |
| `FileSystemBlobOptions` | class | `BasePath` (default `Path.Combine(AppContext.BaseDirectory, "blobs")`). |
| `SliceBlobStoringModule` | module | Registers the resolver, default provider, and typed containers. |

## Usage

Define a container marker type (its name becomes the container):

```csharp
using Slice.BlobStoring;

// Resolves to container name "avatar" (class name minus "Container", lower-cased).
public sealed class AvatarContainer;

// Or override explicitly:
[BlobContainerName("user-avatars")]
public sealed class AvatarContainer2;
```

Inject the typed container and save / read a blob:

```csharp
public sealed class ProfilePhotoHandler(IBlobContainer<AvatarContainer> avatars)
{
    public async Task SaveAsync(string userId, Stream photo, CancellationToken ct)
    {
        await avatars.SaveAsync($"{userId}.png", photo, overrideExisting: true, ct);
    }

    public async Task<Stream?> ReadAsync(string userId, CancellationToken ct)
    {
        return await avatars.GetOrNullAsync($"{userId}.png", ct);
    }
}
```

## Notes

- **Lifetimes:** `BlobContainerNameResolver` and `FileSystemBlobProvider` are singletons; `BlobContainer<>` is transient (it caches the resolved container name in its constructor).
- **Naming convention:** `[BlobContainerName]` wins; else class name minus a trailing `Container` suffix, `ToLowerInvariant()`. E.g. `DocumentsContainer` → `documents`.
- **Save semantics:** with `overrideExisting: false` (the default), saving over an existing blob throws `InvalidOperationException`.
- **`GetOrNullAsync`** returns `null` when the blob is absent (no exception).
- **FileSystem safety:** blob names are normalized (`\` → `/`, leading `/` trimmed) and resolved paths must stay under the container root, otherwise `InvalidOperationException("Invalid blob name.")`.
- **Swapping backends:** reference `Slice.BlobStoring.Azure`, `.Aws`, or `.Minio` and call its `AddSliceBlobStoringXxx(...)` extension, which removes the file-system `IBlobProvider` and registers its own.
