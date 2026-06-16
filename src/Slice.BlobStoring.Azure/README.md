# Slice.BlobStoring.Azure

> Azure Blob Storage backend for Slice.BlobStoring — each Slice container maps to an Azure container.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

This adapter implements `IBlobProvider` against Azure Blob Storage using `BlobServiceClient`. Unlike the S3/MinIO adapters, each Slice container name maps to a distinct Azure blob container (auto-created on first use with no public access). Registering it removes the default file-system provider, so `IBlobContainer<TContainer>` consumers are unaffected.

## Dependencies

- **Slice:** `Slice.BlobStoring`
- **Third-party:** `Azure.Storage.Blobs`

## Module & registration

No `SliceModule` of its own — registration is via the `AddSliceBlobStoringAzure` extension. It removes any existing `IBlobProvider`, registers a `BlobServiceClient` built from the connection string, and registers `AzureBlobProvider` as the singleton `IBlobProvider`.

```csharp
services.AddSliceBlobStoringAzure(connectionString);
```

## Key types

| Type | Kind | Description |
|---|---|---|
| `AzureBlobProvider` | class | `IBlobProvider` over `BlobServiceClient`. Maps each container name to an Azure blob container. |
| `AzureBlobStoringRegistration` | static class | Hosts `AddSliceBlobStoringAzure(this IServiceCollection, string connectionString)`. |

## Usage

```csharp
services.AddSliceBlobStoringAzure(
    "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net");

// Consumers stay backend-agnostic:
public sealed class InvoiceStore(IBlobContainer<InvoiceContainer> invoices)
{
    public Task<Stream?> ReadAsync(string id, CancellationToken ct)
        => invoices.GetOrNullAsync($"{id}.pdf", ct);
}
```

## Notes

- **Container mapping:** each Slice container is a separate Azure blob container. `GetContainerAsync` calls `CreateIfNotExistsAsync(PublicAccessType.None)` before every operation, so containers are created lazily and are private.
- **Lifetime:** `AzureBlobProvider` and the `BlobServiceClient` are singletons.
- **Save semantics:** `SaveAsync` forwards `overrideExisting` to Azure's `UploadAsync(overwrite: ...)`. With `overrideExisting: false`, uploading over an existing blob surfaces Azure's own conflict error (it does not pre-check/throw a Slice `InvalidOperationException` like the S3/MinIO adapters).
- **Not-found handling:** `GetOrNullAsync` checks `ExistsAsync` and returns `null` if absent; otherwise returns the streaming download content. `DeleteAsync` uses `DeleteIfExistsAsync` and returns whether a blob was deleted.
- **Container naming:** Slice container names must satisfy Azure container naming rules (lower-case, etc.); the default resolver already lower-cases names.
