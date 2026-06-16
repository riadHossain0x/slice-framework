# Slice.Dapper

> Dapper raw-SQL access that runs on a Slice DbContext's connection and ambient transaction, sharing the same unit of work as EF Core.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

This project lets a handler drop down to raw SQL with Dapper without breaking out of the unit of work. `IDapperExecutor<TContext>` runs queries on a specific `SliceDbContext`'s ADO.NET connection and enlists in its ambient EF transaction, so Dapper reads/writes are committed (or rolled back) together with EF Core changes. There is one executor per bounded-context DbContext.

## Dependencies

- **Slice:** `Slice.Core`, `Slice.EntityFrameworkCore`
- **Third-party:** `Dapper`, `Microsoft.Extensions.DependencyInjection.Abstractions`

## Module & registration

`SliceDapperModule` `[DependsOn(typeof(SliceEntityFrameworkCoreModule))]` registers the open generic `IDapperExecutor<>` → `DapperExecutor<>` as scoped. Add it to a host/module's dependency chain and inject `IDapperExecutor<YourDbContext>`.

```csharp
[DependsOn(typeof(SliceDapperModule))]
public sealed class CatalogModule : SliceModule;
```

## Key types

| Type | Kind | Description |
|---|---|---|
| `IDapperExecutor<TContext>` | interface | Dapper operations bound to `TContext` (`where TContext : SliceDbContext`). |
| `DapperExecutor<TContext>` | sealed class | Implementation that opens the context connection and enlists in its ambient transaction. |
| `SliceDapperModule` | sealed class | Module registering `IDapperExecutor<>` as scoped. |

### `IDapperExecutor<TContext>` methods

```csharp
Task<IEnumerable<T>> QueryAsync<T>(string sql, object? param = null, CancellationToken ct = default);
Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? param = null, CancellationToken ct = default);
Task<T>  QuerySingleAsync<T>(string sql, object? param = null, CancellationToken ct = default);
Task<int> ExecuteAsync(string sql, object? param = null, CancellationToken ct = default);
Task<T?> ExecuteScalarAsync<T>(string sql, object? param = null, CancellationToken ct = default);
```

## Usage

Inject `IDapperExecutor<MyDbContext>` into a handler and run raw SQL inside the same unit of work as EF Core:

```csharp
public sealed class GetTopProductsHandler(IDapperExecutor<CatalogDbContext> dapper)
    : IRequestHandler<GetTopProducts, IReadOnlyList<ProductSummary>>
{
    public async Task<IReadOnlyList<ProductSummary>> HandleAsync(
        GetTopProducts request, CancellationToken ct)
    {
        var rows = await dapper.QueryAsync<ProductSummary>(
            "SELECT Id, Name, Price FROM Products ORDER BY Sales DESC LIMIT @take",
            new { take = request.Take }, ct);
        return rows.ToList();
    }
}
```

Writes participate in the EF transaction — if the command's unit of work rolls back, the Dapper `ExecuteAsync` rolls back too:

```csharp
await dapper.ExecuteAsync(
    "UPDATE Products SET Price = @price WHERE Id = @id",
    new { id, price }, ct);
```

## Notes

- Registered as **scoped** (open generic), matching the scoped lifetime of the DbContext it wraps.
- Each call invokes `db.GetOpenConnectionAsync(ct)` (opening the connection if needed) and passes `db.GetCurrentTransaction()` to the Dapper `CommandDefinition` — `null` when no ambient transaction is active.
- Because it reuses the EF connection and transaction, no separate connection/transaction is opened: Dapper and EF Core operations form one atomic unit of work.
