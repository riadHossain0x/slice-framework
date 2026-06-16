# Slice.BlobStoring.Postgres

> An `IBlobProvider` that stores blobs as `bytea` rows in a single Postgres table (`slice_blobs`), keyed by (container, name).

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md), the [docs](../../docs/) and the [PostgreSQL stack guide](../../docs/postgresql-stack.md).

## Overview

This package replaces the registered `IBlobProvider` with `PostgresBlobProvider`, which keeps binary content in the same database as everything else — blobs are `bytea` rows in `slice_blobs`, addressed by `(container, name)`. It implements the full save/get/exists/delete surface, with `overrideExisting` enforced on save via an upsert.

## Dependencies

- **Slice:** `Slice.BlobStoring`, `Slice.Postgres`
- **Third-party:** `Npgsql`, `Microsoft.Extensions.DependencyInjection.Abstractions`

## Registration

```csharp
// With a connection string: registers the shared NpgsqlDataSource here too.
services.AddSlicePostgresBlobStoring(connectionString);

// Or omit it to reuse a data source already registered by AddSlicePostgres / AddSlicePostgresStack:
services.AddSlicePostgresBlobStoring();
```

`AddSlicePostgresBlobStoring` contributes the `slice_blobs` DDL via `AddPostgresSchema`, then does the swap: `RemoveAll<IBlobProvider>()` followed by `AddSingleton<IBlobProvider, PostgresBlobProvider>()`. When a connection string is passed it calls `AddSlicePostgres(connectionString)` first; otherwise it reuses the shared data source.

## Key types

| Type | Kind | Description |
|---|---|---|
| `PostgresBlobProvider` | sealed class (`IBlobProvider`) | `SaveAsync`, `GetOrNullAsync`, `ExistsAsync`, `DeleteAsync` over `slice_blobs`; save upserts (or throws when `overrideExisting` is false and the blob exists). |
| `PostgresBlobStoringRegistration` | static class | `AddSlicePostgresBlobStoring(connectionString?)` DI extension. |

## Schema / storage

DDL (`PostgresBlobProvider.Ddl`) is contributed to `AddPostgresSchema`, so `PostgresSchemaInitializer` creates it at startup:

```sql
CREATE TABLE IF NOT EXISTS slice_blobs (
    container text NOT NULL,
    name      text NOT NULL,
    data      bytea NOT NULL,
    PRIMARY KEY (container, name)
);
```

`SaveAsync` uses `INSERT … ON CONFLICT (container, name) DO UPDATE SET data = EXCLUDED.data`. `DeleteAsync` returns `true` when a row was removed; `GetOrNullAsync` returns a `MemoryStream` over the bytes or `null`.

## Usage

```csharp
builder.Services.AddSlicePostgres(connectionString);
builder.Services.AddSlicePostgresBlobStoring();

await blobProvider.SaveAsync("avatars", "user-42.png", stream, overrideExisting: true, ct);

await using var s = await blobProvider.GetOrNullAsync("avatars", "user-42.png", ct);
if (s is not null) { /* ... */ }

var removed = await blobProvider.DeleteAsync("avatars", "user-42.png", ct);
```

## Notes

- **In-database blobs:** content lives as `bytea` in Postgres — convenient for backups/consistency, but not suited to very large objects or high-throughput streaming. `SaveAsync` buffers the whole stream into memory before the insert.
- **`overrideExisting`:** when false and the blob already exists, `SaveAsync` throws `InvalidOperationException`; when true it upserts.
- **Shared pool:** when called without a connection string it reuses the stack's single `NpgsqlDataSource`.
