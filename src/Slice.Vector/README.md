# Slice.Vector

> Storage-agnostic vector-search abstractions plus an offline default embedder for the Slice framework.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md), the [docs](../../docs/) and the [PostgreSQL stack guide](../../docs/postgresql-stack.md).

## Overview

`Slice.Vector` defines the seams for semantic and nearest-neighbour search without binding to any database or embedding provider. `IVectorStore` resolves named, fixed-dimension `IVectorCollection`s that upsert `VectorRecord`s and answer top-K `SearchAsync` queries. Because the store accepts a raw `float[]` embedding, an embedder is optional — but the `IEmbeddingGenerator` seam lets you turn text into vectors. The module ships a deterministic, offline `HashingEmbeddingGenerator` as the default so development and tests need no model or network.

## Dependencies

- **Slice:** `Slice.Core`, `Slice.Modularity`, `Slice.Application`
- **Third-party:** `Microsoft.Extensions.DependencyInjection.Abstractions`

## Registration

`SliceVectorModule` (depends on `SliceApplicationModule`) runs `AddSliceConventions` over its assembly, which registers `HashingEmbeddingGenerator` as the default `IEmbeddingGenerator` (it is an `ISingletonDependency`). No `IVectorStore` is registered here — supply one from an adapter such as `Slice.Vector.Postgres`.

```csharp
[DependsOn(typeof(SliceVectorModule))]
public sealed class MyModule : SliceModule { }
```

## Key types

| Type | Kind | Description |
|---|---|---|
| `VectorDistance` | enum | Distance metric: `Cosine`, `L2`, `InnerProduct`. |
| `VectorRecord` | sealed record | `(string Id, float[] Embedding, string? Content = null, IReadOnlyDictionary<string, object?>? Metadata = null, Guid? TenantId = null)`. |
| `VectorSearchResult` | sealed record | `(VectorRecord Record, double Score)` — score is distance-derived (lower = nearer). |
| `IVectorCollection` | interface | A named, fixed-dimension collection: `Name`, `Dimensions`, `Distance`; `EnsureCreatedAsync`, `UpsertAsync`, `UpsertBatchAsync`, `DeleteAsync`, `SearchAsync(float[] query, int topK, …)`. |
| `IVectorStore` | interface | Entry point: `GetCollection(string name, int dimensions, VectorDistance distance = VectorDistance.Cosine)`. |
| `IEmbeddingGenerator` | interface | Text → vector seam: `Dimensions`, `GenerateAsync(string)`, `GenerateBatchAsync(IReadOnlyList<string>)`. |
| `HashingEmbeddingGenerator` | sealed class | Default offline embedder (`IEmbeddingGenerator`, `ISingletonDependency`), 256 dimensions. |
| `SliceVectorModule` | sealed class (`SliceModule`) | Module registering the default embedder. |

## Usage

```csharp
public sealed class DocSearch(IVectorStore store, IEmbeddingGenerator embedder)
{
    public async Task IndexAsync(string id, string text, CancellationToken ct)
    {
        var collection = store.GetCollection("docs", embedder.Dimensions, VectorDistance.Cosine);
        await collection.EnsureCreatedAsync(ct);

        var embedding = await embedder.GenerateAsync(text, ct);
        await collection.UpsertAsync(new VectorRecord(id, embedding, Content: text), ct);
    }

    public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(string query, CancellationToken ct)
    {
        var collection = store.GetCollection("docs", embedder.Dimensions, VectorDistance.Cosine);
        var queryVector = await embedder.GenerateAsync(query, ct);
        return await collection.SearchAsync(queryVector, topK: 5, ct);
    }
}
```

You can also bypass the embedder entirely and pass any `float[]` you produced elsewhere — the store never sees text.

## Notes

- **Embeddings are optional.** `IVectorCollection` works on raw `float[]`; the embedder is a convenience seam, not a requirement.
- **The default embedder is deterministic but NOT semantic.** `HashingEmbeddingGenerator` uses FNV-1a feature-hashing over lowercased letter/digit tokens into a 256-slot vector, then L2-normalises. Identical text yields identical vectors and shared words pull vectors together, but it has no understanding of meaning. Swap in a real model (e.g. `Slice.Embeddings.OpenAI`) for production semantic search.
- `HashingEmbeddingGenerator` is registered as a singleton via `ISingletonDependency`.
- `Dimensions` must agree across the embedder and the collection — call `store.GetCollection(name, embedder.Dimensions, …)`.
- Tenant-awareness lives in the abstraction: `VectorRecord.TenantId` is optional and adapters are expected to filter searches to the current tenant when one is set.
