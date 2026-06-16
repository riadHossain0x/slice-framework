# Slice.BlobStoring.Aws

> AWS S3 backend for Slice.BlobStoring — all containers live in one bucket, keyed `{container}/{blob}`.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

This adapter implements `IBlobProvider` against Amazon S3 using the AWS SDK (`IAmazonS3`). Every Slice container is stored within a single configured bucket, with the container name used as the object-key prefix (`{container}/{blob}`). Registering it removes the default file-system provider and substitutes the S3-backed one, so existing `IBlobContainer<TContainer>` consumers are unchanged.

## Dependencies

- **Slice:** `Slice.BlobStoring`
- **Third-party:** `AWSSDK.S3`

## Module & registration

No `SliceModule` of its own — registration is via the `AddSliceBlobStoringAws` extension on `IServiceCollection`. It removes any existing `IBlobProvider`, registers `AwsS3BlobOptions`, ensures an `IAmazonS3` client (the supplied one, or a default `AmazonS3Client()` via `TryAddSingleton`), and registers `AwsS3BlobProvider` as the singleton `IBlobProvider`.

```csharp
// Default credentials/region from the AWS SDK chain:
services.AddSliceBlobStoringAws("my-app-bucket");

// Or supply a pre-configured client:
services.AddSliceBlobStoringAws("my-app-bucket", new AmazonS3Client(/* ... */));
```

## Key types

| Type | Kind | Description |
|---|---|---|
| `AwsS3BlobProvider` | class | `IBlobProvider` over `IAmazonS3`. Keys objects as `{container}/{blob}` in `options.Bucket`. |
| `AwsS3BlobOptions` | class | `required string Bucket` — the single bucket all containers share. |
| `AwsBlobStoringRegistration` | static class | Hosts `AddSliceBlobStoringAws(this IServiceCollection, string bucket, IAmazonS3? client = null)`. |

## Usage

```csharp
services.AddSliceBlobStoringAws("my-app-bucket");

// Consumers stay backend-agnostic:
public sealed class ReportStore(IBlobContainer<ReportContainer> reports)
{
    public Task SaveAsync(string id, Stream pdf, CancellationToken ct)
        => reports.SaveAsync($"{id}.pdf", pdf, overrideExisting: true, ct);
}
```

## Notes

- **Single bucket, key prefixing:** the container name is *not* an S3 bucket — it is the key prefix. `reports` + `2026.pdf` becomes object key `reports/2026.pdf`.
- **Lifetime:** `AwsS3BlobProvider` and the `IAmazonS3` client are singletons.
- **Save semantics:** with `overrideExisting: false`, `SaveAsync` first checks `ExistsAsync` and throws `InvalidOperationException` if the blob exists. The `PutObjectRequest` uses `AutoCloseStream = false` (the caller owns the stream).
- **Not-found handling:** `GetOrNullAsync`/`ExistsAsync`/`DeleteAsync` catch `AmazonS3Exception` with `HttpStatusCode.NotFound` and return `null`/`false` accordingly. `GetOrNullAsync` buffers the response into a `MemoryStream` (positioned at 0).
- **Default client:** if no `IAmazonS3` is passed, a parameterless `AmazonS3Client()` is registered, relying on the standard AWS credential/region resolution chain.
