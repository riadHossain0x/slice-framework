# Slice.Domain

> DDD building blocks: entities, aggregate roots, value objects, auditing interfaces, repositories, specifications, domain events, multi-tenancy, and guard clauses.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

`Slice.Domain` provides the tactical Domain-Driven Design primitives that bounded-context modules build on. It defines identity-based `Entity<TKey>` and `AggregateRoot<TKey>` (with a layered audit hierarchy up to `FullAuditedAggregateRoot<TKey>`), structural-equality `ValueObject`, the auditing/multi-tenancy/soft-delete marker interfaces that the persistence layer reads, repository and specification abstractions, the domain/distributed event contracts, an `Ensure` guard helper, and a small set of domain exceptions. It sits directly above `Slice.Core` and below the application/persistence layers.

## Dependencies

- **Slice:** `Slice.Core` (project reference)
- **Third-party:** none beyond the BCL (`System.Linq.Expressions` is used for specifications)

## Module & registration

`Slice.Domain` defines no `SliceModule` and registers no services itself — it is a library of base classes and interfaces. Aggregates, repository implementations, and specifications that you derive from these types are registered by the consuming module (e.g. via `AddSliceConventions(assembly)` for marker-bearing implementations, or explicit registrations such as `services.AddScoped<ILeadRepository, EfLeadRepository>()`).

## Key types

| Type | Kind | Description |
|---|---|---|
| `IEntity` | interface | `object?[] GetKeys()`. |
| `IEntity<TKey>` | interface | Adds `TKey Id { get; }`. |
| `Entity<TKey>` | abstract class | Single-PK base with identity-based equality, `IsTransient()`, `==`/`!=`, `GetKeys()`. |
| `IAggregateRoot` / `IAggregateRoot<TKey>` | interface | Marker for aggregate roots. |
| `AggregateRoot<TKey>` | abstract class | Aggregate base; collects domain/distributed events, carries a `ConcurrencyStamp`, and an `ExtraProperties` bag (`IHasExtraProperties`). |
| `IHasExtraProperties` / `ExtraPropertyDictionary` | interface / class | Schema-less key/value bag persisted as one JSON column. |
| `ExtraPropertyExtensions` | static class | `SetProperty` (chainable) / `GetProperty` / `GetProperty<T>` / `HasProperty` / `RemoveProperty`. |
| `CreationAuditedAggregateRoot<TKey>` | abstract class | Adds `CreationTime`, `CreatorId`. |
| `AuditedAggregateRoot<TKey>` | abstract class | Adds `LastModificationTime`, `LastModifierId`. |
| `FullAuditedAggregateRoot<TKey>` | abstract class | Adds soft-delete (`IsDeleted`, `DeletionTime`, `DeleterId`). |
| `ValueObject` | abstract class | Structural equality via `GetEqualityComponents()`. |
| `IHasConcurrencyStamp` | interface | `string ConcurrencyStamp { get; set; }`. |
| `IHasCreationTime` | interface | `DateTime CreationTime`. |
| `IMayHaveCreator` | interface | `Guid? CreatorId`. |
| `ICreationAuditedObject` | interface | `IHasCreationTime` + `IMayHaveCreator`. |
| `IHasModificationTime` | interface | `DateTime? LastModificationTime`. |
| `IModificationAuditedObject` | interface | Adds `Guid? LastModifierId`. |
| `IAuditedObject` | interface | Creation + modification audit. |
| `ISoftDelete` | interface | `bool IsDeleted`. |
| `IHasDeletionTime` | interface | `DateTime? DeletionTime`, `Guid? DeleterId`. |
| `IDeletionAuditedObject` | interface | Deletion audit. |
| `IFullAuditedObject` | interface | Full audit (creation + modification + deletion). |
| `IMultiTenant` | interface | `Guid? TenantId { get; }` — tenant-scoped stamping/filtering. |
| `IDomainEvent` | interface | Local, in-process event raised by an aggregate. |
| `IDistributedEvent` | interface | Integration event (ETO) for the outbox / other services. |
| `IHasDomainEvents` | interface | Exposes collected domain/distributed events and `Clear*` methods. |
| `IReadRepository<TEntity, TKey>` | interface | Read-only repo: `FindAsync`, `GetAsync`, `GetListAsync`, `GetCountAsync`, `GetQueryableAsync`. |
| `IRepository<TEntity, TKey>` | interface | Read/write repo over an aggregate root: `InsertAsync`, `UpdateAsync`, `DeleteAsync`. |
| `ISpecification<T>` | interface | `ToExpression()`, `IsSatisfiedBy(T)`. |
| `Specification<T>` | abstract class | Composable spec with `And`, `Or`, `Not` and implicit `Expression` conversion. |
| `Ensure` | static class | Guard clauses: `NotNull`, `NotNullOrWhiteSpace`, `True`, `Positive`, `Range`. |
| `DomainException` | abstract class | Base domain failure with stable `Code`. |
| `BusinessRuleException` | class | Invariant violated (HTTP 409); default code `Domain:BusinessRule`. |
| `EntityNotFoundException` | class | Entity missing (HTTP 404); `For<TEntity>(object key)` factory; default code `Domain:NotFound`. |
| `AppValidationException` | class | Domain validation failed (HTTP 400); default code `Domain:Validation`. |
| `ModuleDescriptor` | — | (defined in `Slice.Modularity`, not here.) |

## Usage

Modeling an aggregate with business methods and raised events:

```csharp
using Slice.Domain.Entities;
using Slice.Domain.Events;
using Slice.Domain.Guards;
using Slice.Domain.MultiTenancy;

public sealed record LeadQualifiedEto(Guid LeadId) : IDistributedEvent;
public sealed record LeadQualified(Guid LeadId) : IDomainEvent;

public sealed class Lead : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    private Lead() { }                       // for the ORM

    public Lead(Guid id, string name, Guid? tenantId) : base(id)
    {
        Name = Ensure.NotNullOrWhiteSpace(name, nameof(name), maxLength: 200);
        TenantId = tenantId;
    }

    public string Name { get; private set; } = default!;
    public Guid? TenantId { get; private set; }
    public bool IsQualified { get; private set; }

    public void Qualify()
    {
        Ensure.True(!IsQualified, "Lead is already qualified.", "Lead:AlreadyQualified");
        IsQualified = true;

        AddDomainEvent(new LeadQualified(Id));        // dispatched around SaveChanges
        AddDistributedEvent(new LeadQualifiedEto(Id)); // routed to the outbox
    }
}
```

A value object and a specification:

```csharp
using Slice.Domain.Values;
using Slice.Domain.Specifications;
using System.Linq.Expressions;

public sealed class Money(decimal amount, string currency) : ValueObject
{
    public decimal Amount { get; } = amount;
    public string Currency { get; } = currency;
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }
}

public sealed class QualifiedLeadSpec : Specification<Lead>
{
    public override Expression<Func<Lead, bool>> ToExpression() => l => l.IsQualified;
}

// compose: qualified AND tenant-scoped
var spec = new QualifiedLeadSpec().And(new TenantLeadSpec(tenantId));
var leads = await repository.GetListAsync(spec, ct);
```

## Notes

- **Identity equality:** `Entity<TKey>` compares by `Id` and exact runtime type; two transient entities (default `Id`) are equal only by reference, and `GetHashCode()` falls back to `base.GetHashCode()` while transient. `Id` has a `protected set`.
- **Concurrency:** `AggregateRoot<TKey>.ConcurrencyStamp` defaults to a fresh `Guid.NewGuid().ToString("N")`.
- **Events:** `DomainEvents`/`DistributedEvents` are exposed as `IReadOnlyCollection<>`; add via the `protected AddDomainEvent`/`AddDistributedEvent`, clear via `ClearDomainEvents()`/`ClearDistributedEvents()`. Domain events are dispatched around `SaveChanges`; distributed events (suffix `Eto`) target the outbox.
- **Audit hierarchy:** `FullAuditedAggregateRoot` → `AuditedAggregateRoot` → `CreationAuditedAggregateRoot` → `AggregateRoot`. Audit fields are plain settable properties populated by persistence interceptors, not domain code.
- **Specifications:** `IsSatisfiedBy` compiles the expression each call (in-memory check); `And`/`Or`/`Not` rewrite parameters via an internal `ReplaceParameterVisitor` so the combined expression remains EF-translatable. A `Specification<T>` implicitly converts to `Expression<Func<T, bool>>`.
- **Repositories:** write methods take `bool autoSave = false`; `DeleteAsync` has both entity and key overloads. `IReadRepository.GetQueryableAsync()` returns `Task<IQueryable<TEntity>>`.
- **Exceptions:** all derive from `DomainException` and carry a stable `Code`; the doc comments note the intended HTTP mappings (409 / 404 / 400). `EntityNotFoundException.For<TEntity>(key)` builds a standard message.
- **Guards:** `Ensure.NotNull` and the validation guards throw `AppValidationException`; `Ensure.True` throws `BusinessRuleException` (optionally with a custom code).
