# Slice.BlobStoring.Minio

> MinIO (S3-compatible) backend for Slice.BlobStoring — one bucket, lazy creation, keyed `{container}/{blob}`.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

This adapter implements `IBlobProvider` against MinIO (and other S3-compatible stores) using the `Minio` client. Like the AWS adapter, all Slice containers share a single bucket and the container name becomes the object-key prefix (`{container}/{blob}`). The target bucket is created lazily on first use (once, behind a gate) when `CreateBucketIfNotExists` is set. Verified against a real MinIO container.

## Dependencies

- **Slice:** `Slice.BlobStoring`
- **Third-party:** `Minio`, `Microsoft.Extensions.DependencyInjection.Abstractions`

## Module & registration

No `SliceModule` of its own — registration is via the `AddSliceBlobStoringMinio` extension, which takes an `Action<MinioBlobOptionsBuilder>`. It builds the options, constructs an `IMinioClient` (endpoint, credentials, SSL, optional region), removes any existing `IBlobProvider`, and registers `MinioBlobProvider` as the singleton `IBlobProvider`.

```csharp
services.AddSliceBlobStoringMinio(o =>
{
    o.Endpoint = "localhost:9000";
    o.AccessKey = "minioadmin";
    o.SecretKey = "minioadmin";
    o.Bucket = "slice";
    o.UseSsl = false;
    // o.Region = "us-east-1";
    // o.CreateBucketIfNotExists = true;
});
```

## Key types

| Type | Kind | Description |
|---|---|---|
| `MinioBlobProvider` | class | `IBlobProvider` over `IMinioClient`. Keys objects as `{container}/{blob}` in `options.Bucket`; ensures the bucket exists once. |
| `MinioBlobOptions` | class | `required Endpoint`, `required AccessKey`, `required SecretKey`, `required Bucket`, `bool UseSsl`, `string? Region`, `bool CreateBucketIfNotExists` (default `true`). All `init`-only. |
| `MinioBlobOptionsBuilder` | class | Mutable builder for inline config. Defaults: `Endpoint = "localhost:9000"`, `AccessKey = "minioadmin"`, `SecretKey = "minioadmin"`, `Bucket = "slice"`, `UseSsl = false`, `Region = null`, `CreateBucketIfNotExists = true`. |
| `MinioBlobStoringRegistration` | static class | Hosts `AddSliceBlobStoringMinio(this IServiceCollection, Action<MinioBlobOptionsBuilder> configure)`. |

## Usage

```csharp
services.AddSliceBlobStoringMinio(o =>
{
    o.Endpoint = "minio.internal:9000";
    o.AccessKey = "AKIA...";
    o.SecretKey = "secret...";
    o.Bucket = "app-blobs";
    o.UseSsl = true;
});

// Consumers stay backend-agnostic:
public sealed class ExportStore(IBlobContainer<ExportContainer> exports)
{
    public Task SaveAsync(string name, Stream data, CancellationToken ct)
        => exports.SaveAsync(name, data, overrideExisting: true, ct);
}
```

## Notes

- **Single bucket, key prefixing:** the container name is the key prefix, not a bucket. `exports` + `q1.csv` → object key `exports/q1.csv`.
- **Lazy bucket creation:** every operation calls `EnsureBucketAsync`, which uses a `SemaphoreSlim` gate + `_bucketReady` flag so `BucketExists`/`MakeBucket` runs at most once. If the bucket is missing and `CreateBucketIfNotExists` is `false`, it is left uncreated.
- **Non-seekable streams:** MinIO's `PutObject` requires an object size. `SaveAsync` uses `stream.Length - stream.Position` for seekable streams; for non-seekable streams it buffers into a `MemoryStream` to learn the length. Content type is set to `application/octet-stream`.
- **Save semantics:** with `overrideExisting: false`, `SaveAsync` checks `ExistsAsync` and throws `InvalidOperationException` if the blob exists.
- **Not-found handling:** `GetOrNullAsync`/`ExistsAsync` catch `ObjectNotFoundException` and return `null`/`false`. `GetOrNullAsync` copies into a `MemoryStream` positioned at 0.
- **Lifetime:** `MinioBlobProvider`, `MinioBlobOptions`, and the `IMinioClient` are singletons.
