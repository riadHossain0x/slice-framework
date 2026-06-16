# Domain & persistence

This page covers the DDD building blocks (`Slice.Domain`) and how they are persisted
(`Slice.EntityFrameworkCore`), including the unit of work, auditing, soft-delete, the transactional
outbox/inbox, and the two alternative ORMs that share the EF connection (`Slice.Dapper`,
`Slice.LinqToDB`).

---

## Domain building blocks (`Slice.Domain`)

### Entities and aggregate roots

```csharp
public abstract class Entity<TKey> : IEntity<TKey> { public TKey Id { get; protected set; } }

public abstract class AggregateRoot<TKey> : Entity<TKey>, IHasDomainEvents, IHasConcurrencyStamp
{
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");
    public IReadOnlyCollection<IDomainEvent> DomainEvents { get; }
    public IReadOnlyCollection<IDistributedEvent> DistributedEvents { get; }
    protected void AddDomainEvent(IDomainEvent e);
    protected void AddDistributedEvent(IDistributedEvent e);
    public void ClearDomainEvents();
    public void ClearDistributedEvents();
}
```

An **aggregate root** is the consistency boundary: all changes go through its business methods, never
public setters. It collects two kinds of events:

- **Domain events** (`IDomainEvent`) — in-process, dispatched after save to `IDomainEventHandler<T>`.
- **Distributed events** (`IDistributedEvent`) — integration events written to the outbox in the same
  transaction and published to other services.

### Audited aggregates

Pick the base that matches the auditing you want; the interceptor stamps the fields automatically:

| Base class | Adds |
|---|---|
| `AggregateRoot<TKey>` | events + concurrency stamp |
| `CreationAuditedAggregateRoot<TKey>` | `CreationTime`, `CreatorId` |
| `AuditedAggregateRoot<TKey>` | + `LastModificationTime`, `LastModifierId` |
| `FullAuditedAggregateRoot<TKey>` | + `ISoftDelete` (`IsDeleted`, `DeletionTime`, `DeleterId`) |

Implement `IMultiTenant` (`Guid? TenantId`) to opt an entity into tenant isolation.

### Value objects

```csharp
public sealed class FullName : ValueObject
{
    public string FirstName { get; }
    public string LastName  { get; }
    private FullName(string first, string last) { FirstName = first; LastName = last; }
    public static FullName Create(string first, string last) => new(Ensure.NotNullOrWhiteSpace(first), Ensure.NotNullOrWhiteSpace(last));
    protected override IEnumerable<object?> GetEqualityComponents() { yield return FirstName; yield return LastName; }
}
```

`ValueObject` gives structural equality from `GetEqualityComponents()`. Value objects are mapped as EF
**owned types**, flattened into the parent table.

### A complete aggregate

```csharp
public sealed class Lead : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    private Lead() { }                              // EF

    public Lead(Guid id, Guid? tenantId, FullName name, ContactInfo contact, LeadSource source) : base(id)
    {
        TenantId = tenantId;
        Name = name; Contact = contact; Source = source;
        Status = LeadStatus.New;
        AddDistributedEvent(new LeadCreatedEto(id));   // integration event → outbox
    }

    public Guid? TenantId { get; private set; }
    public FullName Name { get; private set; } = default!;
    public ContactInfo Contact { get; private set; } = default!;
    public LeadStatus Status { get; private set; }
    public LeadSource Source { get; private set; }

    public void Qualify()                           // business method enforces the invariant
    {
        if (Status != LeadStatus.New)
            throw new BusinessRuleException("Only new leads can be qualified.");
        Status = LeadStatus.Qualified;
        AddDomainEvent(new LeadQualifiedEvent(Id));  // in-process event
    }
}
```

### Extra properties (schema-less data)

Every `AggregateRoot<TKey>` implements `IHasExtraProperties` — an `ExtraProperties` key/value bag
persisted as a **single JSON column**, for ad-hoc data without a migration (ABP-style). The ASP.NET
Identity tables (`SliceUser`/`SliceRole`) carry it too.

```csharp
lead.SetProperty("source", "import")        // chainable; values are object?
    .SetProperty("score", 87);

var source = lead.GetProperty<string>("source");   // typed read, tolerant of the JSON round-trip
var score  = lead.GetProperty<int>("score");
bool has   = lead.HasProperty("score");
lead.RemoveProperty("source");
```

The EF mapping is automatic: `SliceDbContext.OnModelCreating` calls `ConfigureExtraProperties(provider)`,
which maps the `ExtraProperties` column (a `jsonb` column on Npgsql, text elsewhere) on every
`IHasExtraProperties` entity, with a value converter **and** a `ValueComparer` so edits to the
dictionary are change-tracked.

**Server-side filtering** (string-valued keys) via `WhereExtraProperty`, translated to the provider's
JSON extraction (`json_extract` on SQLite, `jsonb_extract_path_text` on Postgres):

```csharp
var imported = await repository.GetQueryableAsync()
    .Result.WhereExtraProperty("source", "import").ToListAsync(ct);
```

> Because every aggregate gains the column, a fresh `EnsureCreated`/migrated database includes it
> automatically; an existing database needs a migration to add `ExtraProperties`. Numeric/complex
> filtering beyond string equality, or other providers, fall back to loading + `GetProperty<T>` or raw SQL.

### Repositories & specifications

The repository interface lives **with the domain**:

```csharp
public interface IReadRepository<TEntity, in TKey> where TEntity : class, IEntity<TKey>
{
    Task<TEntity?> FindAsync(TKey id, CancellationToken ct = default);
    Task<TEntity>  GetAsync(TKey id, CancellationToken ct = default);
    Task<List<TEntity>> GetListAsync(ISpecification<TEntity>? spec = null, CancellationToken ct = default);
    Task<long> GetCountAsync(ISpecification<TEntity>? spec = null, CancellationToken ct = default);
    Task<IQueryable<TEntity>> GetQueryableAsync();
}
public interface IRepository<TEntity, TKey> : IReadRepository<TEntity, TKey>
    where TEntity : class, IAggregateRoot<TKey>
{
    Task<TEntity> InsertAsync(TEntity e, bool autoSave = false, CancellationToken ct = default);
    Task<TEntity> UpdateAsync(TEntity e, bool autoSave = false, CancellationToken ct = default);
    Task DeleteAsync(TEntity e, bool autoSave = false, CancellationToken ct = default);
    Task DeleteAsync(TKey id, bool autoSave = false, CancellationToken ct = default);
}
```

`ISpecification<T>` composes query predicates (`And`/`Or`/`Not`). The `Ensure` guard class
(`Ensure.NotNull`, `Ensure.NotNullOrWhiteSpace`, …) validates invariants in constructors/methods and
throws domain exceptions (`DomainException`, `BusinessRuleException`, `EntityNotFoundException`).

---

## Persistence (`Slice.EntityFrameworkCore`)

### The DbContext

```csharp
public sealed class CrmDbContext(DbContextOptions<CrmDbContext> options, ICurrentTenant tenant, IDataFilter filter)
    : SliceDbContext(options, tenant, filter)
{
    public DbSet<Lead> Leads => Set<Lead>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Lead>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
            e.OwnsOne(x => x.Name, n =>     // value object → owned type
            {
                n.Property(p => p.FirstName).HasColumnName("FirstName");
                n.Property(p => p.LastName).HasColumnName("LastName");
            });
            e.Navigation(x => x.Name).IsRequired();
        });
        base.OnModelCreating(b);            // MUST call last — applies the global filters + outbox/inbox
    }
}
```

`SliceDbContext`:

- takes `ICurrentTenant` + `IDataFilter` and applies **global query filters** to every entity that
  implements `ISoftDelete` (hides `IsDeleted` rows) and/or `IMultiTenant` (restricts to the current
  tenant). The filters reference instance members so EF re-evaluates them per query.
- carries the `OutboxMessage`/`InboxMessage` tables (`SliceOutbox`/`SliceInbox`).
- implements `IUnitOfWork` (the UoW behavior resolves it and calls `SaveChangesAsync`).
- exposes the ADO.NET connection + ambient transaction for alternative ORMs:
  `GetDbConnection()`, `GetCurrentTransaction()`, `GetOpenConnectionAsync(ct)`.

> Always call `base.OnModelCreating(b)` **last** so the soft-delete/tenant filters and outbox/inbox
> mappings are applied after your entity configuration.

### Registering the context

```csharp
services.AddSliceDbContext<CrmDbContext>(o => o.UseSqlite(connectionString));
```

`AddSliceDbContext<T>`:

1. registers the `DbContext` and attaches the framework interceptors (`SliceAuditingInterceptor`,
   `DomainEventInterceptor`);
2. registers the context as `IUnitOfWork` (scoped) so the UoW behavior flushes it;
3. starts the `OutboxProcessor<T>` hosted service for that context.

### Repositories

Derive a concrete repository from `EfRepository`:

```csharp
public sealed class EfLeadRepository(CrmDbContext db) : EfRepository<CrmDbContext, Lead, Guid>(db), ILeadRepository
{
    // optional: eager-load children for write operations
    protected override IQueryable<Lead> WithDetails(IQueryable<Lead> q) => q.Include(x => x.Notes);
}
```

Register it (`services.AddScoped<ILeadRepository, EfLeadRepository>()`).

### The unit of work in practice

Handlers do **not** call `SaveChanges`. They mutate aggregates / insert with `autoSave: false`, and
the `UnitOfWorkBehavior` (order 500) commits after a successful `Result`:

```csharp
var lead = new Lead(guids.Create(), tenant.Id, name, contact, source);
await repository.InsertAsync(lead, autoSave: false, ct);
return Result<Guid>.Success(lead.Id);
// UnitOfWorkBehavior → SaveChangesAsync → interceptors run
```

On commit:

- **`SliceAuditingInterceptor`** stamps `CreationTime`/`CreatorId` on inserts and
  `LastModificationTime`/`LastModifierId` on updates (using `IClock` + `ICurrentUser`), regenerates the
  concurrency stamp, and converts deletes of `ISoftDelete` entities into `IsDeleted = true`.
- **`DomainEventInterceptor`** writes each aggregate's `IDistributedEvent`s as `OutboxMessage` rows in
  the **same transaction**, then (after save) dispatches `IDomainEvent`s in-process via the local bus.

---

## Transactional outbox & inbox

Integration events are never published directly from a handler. They are written to the outbox inside
the business transaction, guaranteeing they persist iff the business change persists.

```
SaveChanges (one transaction)
 ├─ business rows
 └─ SliceOutbox rows  ← DomainEventInterceptor serialized the aggregates' IDistributedEvents
        │
        ▼  OutboxProcessor<TContext> (hosted service, polls; takes IDistributedLock for single-runner)
   IDistributedEventPublisher.PublishAsync(event, messageId)  → local loopback OR a broker transport
```

On the consuming side, the **inbox** deduplicates: `IInboxStore.TryMarkProcessedAsync(messageId)`
returns false for a message already handled. The EF inbox (`EfInboxStore`, `SliceInbox` table) makes
this durable. See [Messaging & events](messaging.md).

---

## Alternative ORMs sharing the unit of work

Sometimes EF isn't the right tool for a read (complex SQL) or a bulk write. Slice lets **Dapper** and
**LinqToDB** run on the *same connection and ambient transaction* as the EF context, so they
participate in the same unit of work — a query sees uncommitted EF changes, and a rollback discards
everyone's writes.

### Dapper (`Slice.Dapper`)

```csharp
[DependsOn(typeof(SliceDapperModule))]        // on your module
// …
public sealed class LeadStatsHandler(IDapperExecutor<CrmDbContext> dapper)
    : IQueryHandler<LeadStatsQuery, Result<int>>
{
    public async Task<Result<int>> HandleAsync(LeadStatsQuery q, CancellationToken ct)
    {
        var count = await dapper.ExecuteScalarAsync<long>(
            "select count(*) from Leads where Status = @s", new { s = (int)LeadStatus.Qualified }, ct);
        return Result<int>.Success((int)count);
    }
}
```

`IDapperExecutor<TContext>` exposes `QueryAsync<T>`, `QueryFirstOrDefaultAsync<T>`, `QuerySingleAsync<T>`,
`ExecuteAsync`, `ExecuteScalarAsync<T>` — each opens the context connection (if needed) and enlists in
its current transaction.

### LinqToDB (`Slice.LinqToDB`)

LinqToDB is a genuinely different ORM. Register it with the matching data provider for your database
(LinqToDB can't infer it from an EF connection):

```csharp
[DependsOn(typeof(SliceLinqToDbModule))]
// in ConfigureServices:
services.AddSliceLinqToDb<CrmDbContext>(SQLiteTools.GetDataProvider(ProviderName.SQLiteMS));
```

```csharp
public sealed class BulkArchiveHandler(ISliceDataConnectionFactory<CrmDbContext> factory)
    : ICommandHandler<BulkArchiveCommand, Result>
{
    public async Task<Result> HandleAsync(BulkArchiveCommand c, CancellationToken ct)
    {
        using var dc = await factory.CreateAsync(ct);   // DataConnection over the EF connection + tx
        var n = dc.Query<int>("update Leads set Status = 99 where CreationTime < @cutoff",
                              new { cutoff = c.Cutoff }).First();
        return Result.Success();
    }
}
```

> Because all three ORMs share the connection and the ambient transaction, the UoW commit (or a manual
> `BeginTransactionAsync` + rollback) applies to all of them atomically.
