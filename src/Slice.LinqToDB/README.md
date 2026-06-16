# Slice.LinqToDB

> A LinqToDB `DataConnection` factory bound to a Slice DbContext's connection and transaction, so LinqToDB queries share the same unit of work as EF Core.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

This project adds LinqToDB as a second ORM over the same connection as EF Core. `ISliceDataConnectionFactory<TContext>` creates a LinqToDB `DataConnection` bound to a `SliceDbContext`'s ADO.NET connection and ambient transaction, so LinqToDB queries run inside the same unit of work and transaction as EF Core changes — a genuinely different ORM over one shared connection. The host supplies the LinqToDB `IDataProvider` matching its database; it is held per context in `SliceLinqToDbOptions<TContext>`.

## Dependencies

- **Slice:** `Slice.Core`, `Slice.EntityFrameworkCore`
- **Third-party:** `linq2db`, `Microsoft.Extensions.DependencyInjection.Abstractions`

## Module & registration

`SliceLinqToDbModule` `[DependsOn(typeof(SliceEntityFrameworkCoreModule))]` declares the dependency; each bounded context registers its factory with `AddSliceLinqToDb<TContext>(IDataProvider)`, passing the LinqToDB data provider that matches its database.

```csharp
using LinqToDB;
using LinqToDB.DataProvider.SQLite;

[DependsOn(typeof(SliceLinqToDbModule))]
public sealed class CatalogModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddSliceLinqToDb<CatalogDbContext>(
            SQLiteTools.GetDataProvider(ProviderName.SQLiteMS));
    }
}
```

## Key types

| Type | Kind | Description |
|---|---|---|
| `ISliceDataConnectionFactory<TContext>` | interface | `CreateAsync(ct)` returns a LinqToDB `DataConnection` bound to `TContext`'s connection/transaction. |
| `SliceDataConnectionFactory<TContext>` | sealed class | Implementation reusing the EF connection (and transaction if active) without owning its lifetime. |
| `SliceLinqToDbOptions<TContext>` | sealed class | Holds the chosen `IDataProvider` for `TContext`. |
| `SliceLinqToDbModule` | sealed class | Module depending on `SliceEntityFrameworkCoreModule`. |
| `SliceLinqToDbRegistration` | static class | `AddSliceLinqToDb<TContext>(IDataProvider)` extension. |

## Usage

Inject `ISliceDataConnectionFactory<MyDbContext>` and create a connection inside a handler — it shares the EF unit of work:

```csharp
public sealed class GetActiveProductsHandler(
    ISliceDataConnectionFactory<CatalogDbContext> factory)
    : IRequestHandler<GetActiveProducts, IReadOnlyList<Product>>
{
    public async Task<IReadOnlyList<Product>> HandleAsync(
        GetActiveProducts request, CancellationToken ct)
    {
        await using var dc = await factory.CreateAsync(ct);
        return await dc.GetTable<Product>()
            .Where(p => p.IsActive)
            .ToListAsync(ct);
    }
}
```

## Notes

- The host **must** pass the LinqToDB `IDataProvider` matching the database used by the EF context (e.g. `SQLiteTools.GetDataProvider(ProviderName.SQLiteMS)`, `PostgreSQLTools.GetDataProvider()`). A mismatched provider produces wrong SQL.
- `SliceLinqToDbOptions<TContext>` is registered as a **singleton**; the factory (`ISliceDataConnectionFactory<TContext>`) is **scoped**.
- `CreateAsync` opens the EF connection if needed, then builds `DataOptions`: `UseTransaction(provider, transaction)` when an ambient EF transaction is active, otherwise `UseConnection(provider, connection, disposeConnection: false)`. The factory never owns the connection/transaction lifetime — EF does.
