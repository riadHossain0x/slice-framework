# Slice.Postgres

> Shared PostgreSQL foundation for the Slice stack: one pooled `NpgsqlDataSource` plus an idempotent-DDL schema initializer.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md), the [docs](../../docs/) and the [PostgreSQL stack guide](../../docs/postgresql-stack.md).

## Overview

This package is the common base every Slice Postgres adapter (caching, distributed locking, blob storing, EF Core) builds on. It registers a single shared `NpgsqlDataSource` — one connection pool reused across the whole stack — and a hosted `PostgresSchemaInitializer` that runs each adapter's idempotent DDL once at startup, guarded by a session advisory lock so concurrent instances don't race. It owns no table of its own; adapters contribute their schema via `AddPostgresSchema(ddl)`.

## Dependencies

- **Slice:** `Slice.Core`, `Slice.Modularity`
- **Third-party:** `Npgsql`, `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Hosting.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`

## Registration

```csharp
// Registers the shared NpgsqlDataSource (TryAddSingleton — first caller wins) and the schema initializer.
services.AddSlicePostgres(connectionString);

// Optional NpgsqlDataSourceBuilder hook (only applied when this call builds the data source):
services.AddSlicePostgres(connectionString, b => b.UseVector());

// Adapters contribute idempotent DDL that the initializer runs at startup:
services.AddPostgresSchema("CREATE TABLE IF NOT EXISTS ...;");
```

`AddSlicePostgres` is safe to call multiple times: the data source is added with `TryAddSingleton<NpgsqlDataSource>` and the hosted initializer is de-duplicated by implementation type, so the whole stack shares a single pool. The other Slice Postgres adapters (`AddSlicePostgresCache`, `AddSlicePostgresDistributedLock`, `AddSlicePostgresBlobStoring`) call this internally when given a connection string, and reuse the already-registered data source when not.

## Key types

| Type | Kind | Description |
|---|---|---|
| `IPostgresSchema` | interface | A contributor of idempotent DDL (`string Ddl { get; }`); one per adapter. |
| `PostgresSchemaInitializer` | sealed class (`IHostedService`) | Runs every registered `IPostgresSchema.Ddl` once on `StartAsync`, under a session advisory lock. |
| `SlicePostgresRegistration` | static class | `AddSlicePostgres(connectionString, configure?)` and `AddPostgresSchema(ddl)` DI extensions. |

## Schema / storage

None of its own. It runs the DDL contributed by other packages (e.g. `slice_cache`, `slice_blobs`). On startup `PostgresSchemaInitializer` opens one connection, takes `pg_advisory_lock(4242424242)`, executes each non-empty contributor's DDL in turn, then `pg_advisory_unlock`s in a `finally`. Empty/whitespace DDL is skipped, and if there are no contributors it returns without opening a connection.

## Usage

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSlicePostgres(
    builder.Configuration.GetConnectionString("Default")!,
    configure: b => b.UseVector());   // pgvector, applied only when this call builds the data source

// An adapter registers its idempotent DDL:
builder.Services.AddPostgresSchema("""
    CREATE TABLE IF NOT EXISTS my_table (id text PRIMARY KEY, data bytea NOT NULL);
    """);
```

## Notes

- **Shared pool:** the `NpgsqlDataSource` is a singleton; pass the `configure` hook (or use `AddSlicePostgresStack`) on the first/controlling registration, since it is only applied when this call actually builds the data source.
- **Advisory lock key** `4_242_424_242L` is shared by all Slice schema initializers, so DDL runs serialized across app instances.
- **Idempotency is the contributor's responsibility:** DDL must be safe to run repeatedly (`CREATE TABLE IF NOT EXISTS`, `CREATE EXTENSION IF NOT EXISTS`, …) and may contain multiple statements.
