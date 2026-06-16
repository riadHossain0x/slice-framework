# Slice.EntityFrameworkCore

> EF Core persistence for Slice: a unit-of-work `SliceDbContext`, generic repository, audit/domain-event interceptors, transactional outbox/inbox, and database-per-tenant wiring.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

This project supplies the EF Core implementation of Slice's persistence abstractions. `SliceDbContext` is the base context every bounded context derives from: it implements `IUnitOfWork`, applies soft-delete and multi-tenant global query filters, carries the transactional outbox/inbox tables, and exposes its raw ADO.NET connection/transaction so sibling ORMs (Dapper, LinqToDB) can share the same unit of work. `EfRepository<TContext, TEntity, TKey>` is the generic aggregate repository, audit stamping and event dispatch run through save-changes interceptors, and the registration helpers cover both single-database and database-per-tenant setups.

## Dependencies

- **Slice:** `Slice.Core`, `Slice.Domain`, `Slice.Application`, `Slice.EventBus`
- **Third-party:** `Microsoft.EntityFrameworkCore.Relational`, `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Hosting.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`

## Module & registration

`SliceEntityFrameworkCoreModule` `[DependsOn(typeof(SliceApplicationModule))]` registers the interceptors (`SliceAuditingInterceptor`, `DomainEventInterceptor`) and the local event bus by convention. Each bounded context registers its own context with `AddSliceDbContext<TContext>` (single database) or `AddSliceMultiTenantDbContext<TContext>` (database-per-tenant).

```csharp
[DependsOn(typeof(SliceEntityFrameworkCoreModule))]
public sealed class CatalogModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddSliceDbContext<CatalogDbContext>(o =>
            o.UseSqlite("Data Source=catalog.db"));

        // optional: EF-backed inbox dedup for incoming distributed events
        context.Services.AddSliceInbox<CatalogDbContext>();
    }
}
```

## Key types

| Type | Kind | Description |
|---|---|---|
| `SliceDbContext` | abstract class | Base `DbContext` implementing `IUnitOfWork`; applies soft-delete + multi-tenant query filters, maps the `ExtraProperties` JSON column, owns the `OutboxMessage`/`InboxMessage` sets, and exposes the ADO.NET connection/transaction. |
| `ConfigureExtraProperties` / `WhereExtraProperty` | extensions | Map the `ExtraProperties` JSON column (jsonb on Npgsql) on every `IHasExtraProperties` entity; filter by a string-valued key server-side (`json_extract` / `jsonb_extract_path_text`). |
| `EfRepository<TContext, TEntity, TKey>` | class | Generic `IRepository<TEntity, TKey>` over an `IAggregateRoot<TKey>`; override `WithDetails` to eager-load children. |
| `SliceAuditingInterceptor` | sealed class | `SaveChangesInterceptor` (`ISingletonDependency`) that stamps audit fields, regenerates concurrency stamps, and converts hard deletes of `ISoftDelete` entities into soft deletes. |
| `DomainEventInterceptor` | sealed class | `SaveChangesInterceptor` (`ISingletonDependency`) that writes `IDistributedEvent`s to the outbox in-transaction and dispatches `IDomainEvent`s locally after save. |
| `OutboxMessage` | sealed class | Persisted integration-event row (`Id`, `EventType`, `Payload`, `CreatedAt`, `ProcessedAt`, `RetryCount`, `Error`); table `SliceOutbox`. |
| `OutboxProcessor<TContext>` | sealed class | `BackgroundService` that polls a context's outbox and delivers pending messages via `IDistributedEventPublisher` (one per context). |
| `InboxMessage` | sealed class | Processed-message-id record (`MessageId`, `ProcessedAt`); table `SliceInbox`. |
| `EfInboxStore<TContext>` | sealed class | `IInboxStore` for at-least-once dedup; first writer of a message id wins. |
| `ITenantConnectionStore` | interface | Maps a tenant to a dedicated connection string; `null` means it shares the host database. |
| `NullTenantConnectionStore` | sealed class | Default `ITenantConnectionStore` (`ISingletonDependency`) — no tenant has a dedicated database. |
| `InMemoryTenantConnectionStore` | sealed class | In-memory tenant→connection-string map (seedable, mutable via `Set`). |
| `ITenantConnectionResolver` | interface | Resolves the connection string for the current tenant, falling back to the default. |
| `TenantConnectionResolver` | sealed class | Default resolver (`ISingletonDependency`) backed by an `ITenantConnectionStore`. |
| `SliceEntityFrameworkCoreModule` | sealed class | The persistence module. |
| `SliceDbContextRegistration` | static class | `AddSliceDbContext<T>` / `AddSliceMultiTenantDbContext<T>` extensions. |
| `TenantConnectionRegistration` | static class | `AddTenantConnectionStrings(map)` extension. |
| `InboxRegistration` | static class | `AddSliceInbox<T>` extension. |

### `SliceDbContext` accessors

```csharp
protected SliceDbContext(DbContextOptions options, ICurrentTenant currentTenant, IDataFilter dataFilter);

public DbSet<OutboxMessage> OutboxMessages { get; }

public DbConnection GetDbConnection();                 // Database.GetDbConnection()
public DbTransaction? GetCurrentTransaction();          // ambient EF transaction as ADO.NET, or null
public Task<DbConnection> GetOpenConnectionAsync(CancellationToken ct = default); // opens if needed
```

The global filters reference instance members (`CurrentTenantId`, `IsSoftDeleteFilterEnabled`, `IsMultiTenantFilterEnabled`) so EF re-evaluates them per query rather than baking a constant into the cached model. Filters are applied automatically to every non-owned entity implementing `ISoftDelete` and/or `IMultiTenant`.

## Usage

### Defining a bounded-context DbContext + repository

```csharp
public sealed class CatalogDbContext : SliceDbContext
{
    public CatalogDbContext(DbContextOptions<CatalogDbContext> options,
        ICurrentTenant currentTenant, IDataFilter dataFilter)
        : base(options, currentTenant, dataFilter) { }

    public DbSet<Product> Products => Set<Product>();
}

// Custom repository overriding WithDetails to eager-load children
public sealed class ProductRepository(CatalogDbContext db)
    : EfRepository<CatalogDbContext, Product, Guid>(db), IProductRepository
{
    protected override IQueryable<Product> WithDetails(IQueryable<Product> query)
        => query.Include(p => p.Variants);
}
```

Repository writes (`InsertAsync`/`UpdateAsync`/`DeleteAsync`) track changes by default; the unit-of-work behavior flushes them after a command. Pass `autoSave: true` to save immediately. `DeleteAsync` is soft-delete-aware via the auditing interceptor.

### Database-per-tenant registration

```csharp
context.Services.AddSliceMultiTenantDbContext<CatalogDbContext>(
    defaultConnectionString: "Data Source=host.db",
    configure: (o, cs) => o.UseSqlite(cs));

// register the tenant -> dedicated-database map
context.Services.AddTenantConnectionStrings(new Dictionary<Guid, string>
{
    [tenantA] = "Data Source=tenant-a.db",
    // tenants not listed fall back to the default (host) database
});
```

Per scope/request the connection string is resolved from `ICurrentTenant` via `ITenantConnectionResolver`: a tenant with a dedicated database in the store uses it, otherwise the default (host) string is used and row-level isolation still applies through the query filter.

## Notes

- `AddSliceDbContext` / `AddSliceMultiTenantDbContext` also register `IUnitOfWork` (scoped, resolving to the context) and an `OutboxProcessor<TContext>` hosted service per context.
- The interceptors are singletons; `EfInboxStore<TContext>` and the per-tenant context options are scoped.
- `OutboxProcessor<TContext>` polls every 1s, batches of 50, ordered by `CreatedAt`, retrying up to `MaxRetries` (5). It guards each drain with an `IDistributedLock` keyed `outbox:{ContextName}` so only one node drains at a time (no-op lock by default).
- `OutboxMessage.Id` uses `Guid.CreateVersion7()`; payloads are JSON-serialized and the event type is stored as its assembly-qualified name.
- Default `ITenantConnectionStore` is `NullTenantConnectionStore`; `AddTenantConnectionStrings` removes it and swaps in an `InMemoryTenantConnectionStore`.
- Sibling ORMs (see `Slice.Dapper`, `Slice.LinqToDB`) attach to `GetDbConnection()` / `GetCurrentTransaction()` to share this context's unit of work.
