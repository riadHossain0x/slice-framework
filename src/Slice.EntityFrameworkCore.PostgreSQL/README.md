# Slice.EntityFrameworkCore.PostgreSQL

> Wires a Slice EF Core `DbContext` onto PostgreSQL, reusing the stack's shared connection pool.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md), the [docs](../../docs/) and the [PostgreSQL stack guide](../../docs/postgresql-stack.md).

## Overview

This package adapts the Slice EF Core layer to PostgreSQL via `Npgsql.EntityFrameworkCore.PostgreSQL`. It exposes `UseSlicePostgres` extensions on `DbContextOptionsBuilder` so a `SliceDbContext` — and with it the transactional outbox, inbox, auth and management — all run on Postgres. The `NpgsqlDataSource` overload lets EF share the single pool registered by `Slice.Postgres`, rather than opening its own.

## Dependencies

- **Slice:** `Slice.EntityFrameworkCore`, `Slice.Postgres`
- **Third-party:** `Npgsql.EntityFrameworkCore.PostgreSQL`

## Registration

```csharp
// Preferred: resolve the shared NpgsqlDataSource from DI so EF reuses the stack's pool.
services.AddSliceDbContext<MyDbContext>((sp, o) =>
    o.UseSlicePostgres(sp.GetRequiredService<NpgsqlDataSource>()));

// Or configure straight from a connection string (opens its own pool):
services.AddSliceDbContext<MyDbContext>(o =>
    o.UseSlicePostgres(connectionString));
```

The `AddSliceDbContext<T>(Action<IServiceProvider, DbContextOptionsBuilder>)` overload pairs with the `NpgsqlDataSource` overload of `UseSlicePostgres`, letting the data source be resolved from DI. Register the shared data source first via `AddSlicePostgres(connectionString)` (or `AddSlicePostgresStack`).

## Key types

| Type | Kind | Description |
|---|---|---|
| `SlicePostgresEfExtensions` | static class | `UseSlicePostgres(DbContextOptionsBuilder, NpgsqlDataSource)` and `UseSlicePostgres(DbContextOptionsBuilder, string)` — both wrap `UseNpgsql`. |

## Schema / storage

N/A — this package configures the EF Core provider only. Table/schema creation belongs to EF migrations of the `SliceDbContext` and its features (outbox, inbox, auth, management), not to this adapter.

## Usage

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSlicePostgres(builder.Configuration.GetConnectionString("Default")!);

builder.Services.AddSliceDbContext<AppDbContext>((sp, o) =>
    o.UseSlicePostgres(sp.GetRequiredService<NpgsqlDataSource>()));
```

## Notes

- **Two overloads, two pooling behaviours:** the `NpgsqlDataSource` overload reuses the stack's single shared pool; the `string` overload makes EF open its own. Prefer the data-source overload when composing the Postgres stack.
- Both overloads are thin wrappers over Npgsql's `UseNpgsql` — any further provider tuning is done with the standard Npgsql EF Core options.
