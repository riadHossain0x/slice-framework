# Slice.Vector.Postgres

> A PostgreSQL + pgvector implementation of the Slice `IVectorStore`.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md), the [docs](../../docs/) and the [PostgreSQL stack guide](../../docs/postgresql-stack.md).

## Overview

`Slice.Vector.Postgres` backs the `Slice.Vector` abstractions with PostgreSQL and the [`pgvector`](https://github.com/pgvector/pgvector) extension. Each collection maps to its own table (`slice_vec_{name}`) holding the embedding as a `vector(dim)` column alongside content, JSONB metadata and a tenant id. An HNSW index is created per collection using the operator class that matches the chosen distance metric, and searches use the corresponding pgvector distance operator with `ORDER BY … LIMIT topK`. Tenant filtering is applied automatically from `ICurrentTenant`.

## Dependencies

- **Slice:** `Slice.Vector`, `Slice.Core`, `Slice.Postgres`
- **Third-party:** `Microsoft.Extensions.DependencyInjection.Abstractions`, `Npgsql`, `Pgvector`

## Registration

```csharp
// Build the shared data source here (with UseVector()) from a connection string:
services.AddSlicePostgresVectorStore("Host=localhost;Database=app;Username=postgres;Password=postgres");

// Or, if the data source is already built with vector support (e.g. via AddSlicePostgresStack),
// register the store against it without a connection string:
services.AddSlicePostgresVectorStore();
```

`AddSlicePostgresVectorStore(connectionString?)` registers `PostgresVectorStore` as a singleton `IVectorStore`. When a connection string is passed it calls `AddSlicePostgres(connectionString, b => b.UseVector())` to build the shared `NpgsqlDataSource` with pgvector type support.

## Key types

| Type | Kind | Description |
|---|---|---|
| `PostgresVectorStore` | sealed class (`IVectorStore`) | `(NpgsqlDataSource, ICurrentTenant)`; `GetCollection` returns a `PostgresVectorCollection`. |
| `PostgresVectorCollection` | sealed class (`IVectorCollection`) | One table per collection (`slice_vec_{name}`) with an HNSW index; full upsert/delete/search over pgvector. |
| `PostgresVectorStoreRegistration` | static class | Hosts the `AddSlicePostgresVectorStore(this IServiceCollection, string? connectionString = null)` extension. |

## Usage

```csharp
public sealed class Indexer(IVectorStore store)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var docs = store.GetCollection("docs", dimensions: 1536, VectorDistance.Cosine);
        await docs.EnsureCreatedAsync(ct); // creates extension, table and HNSW index

        await docs.UpsertBatchAsync(new[]
        {
            new VectorRecord("a", embeddingA, Content: "first",  Metadata: new Dictionary<string, object?> { ["lang"] = "en" }),
            new VectorRecord("b", embeddingB, Content: "second"),
        }, ct);

        var hits = await docs.SearchAsync(queryEmbedding, topK: 5, ct);
        // hits[i].Score is the pgvector distance (lower = nearer); hits[i].Record carries id/content/metadata/tenant.
    }
}
```

### Schema and operators

`EnsureCreatedAsync` runs:

```sql
CREATE EXTENSION IF NOT EXISTS vector;
CREATE TABLE IF NOT EXISTS slice_vec_{name} (
    id        text PRIMARY KEY,
    embedding vector({dim}) NOT NULL,
    content   text NULL,
    metadata  jsonb NULL,
    tenant_id uuid NULL
);
CREATE INDEX … ON slice_vec_{name} USING hnsw (embedding {op_class});
CREATE INDEX … ON slice_vec_{name} (tenant_id);
```

| `VectorDistance` | Operator | HNSW op-class |
|---|---|---|
| `Cosine` | `<=>` | `vector_cosine_ops` |
| `L2` | `<->` | `vector_l2_ops` |
| `InnerProduct` | `<#>` | `vector_ip_ops` |

## Notes

- **`UseVector()` is required on the data source.** Pgvector params (`Pgvector.Vector`) only bind if the `NpgsqlDataSource` was built with `UseVector()`. The connection-string overload of `AddSlicePostgresVectorStore` does this for you; otherwise ensure the shared data source (e.g. from the Postgres stack with VectorStore enabled) was built with vector support.
- **Type-catalog reload gotcha.** The `vector` type does not exist until the extension is installed, so `EnsureCreatedAsync` calls `dataSource.ReloadTypesAsync()` after creating the extension. This refreshes Npgsql's type catalog so subsequent commands can bind `Pgvector.Vector` parameters. Always call `EnsureCreatedAsync` before upsert/search on a fresh database.
- **Tenant filtering.** On upsert, `TenantId` falls back to `currentTenant.Id` when the record leaves it null. On search, when `currentTenant.Id` is set, a `WHERE tenant_id = @tid` clause is added automatically; with no current tenant, all rows are eligible.
- `UpsertAsync` delegates to `UpsertBatchAsync`, which uses `INSERT … ON CONFLICT (id) DO UPDATE` inside a transaction. Metadata is stored as JSONB; collection names are sanitised (non-alphanumeric → `_`, lowercased) into the table name.
- The store is registered as a singleton; collections are lightweight objects created per `GetCollection` call.
