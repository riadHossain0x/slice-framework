# Architecture

Slice is built on two ideas that pull in different directions and are deliberately balanced:

- **Vertical Slice Architecture (VSA)** — organise code by *feature*, not by technical layer. Each
  use case (a command or query, its validator, its handler, and its thin controller) lives together
  in one folder and changes together. There is no `Services/`, `Dtos/`, `Interfaces/` spread.
- **Domain-Driven Design (DDD)** — the heart of each bounded context is a rich domain model:
  aggregates with invariants enforced by business methods, value objects, and domain events. The
  domain has no dependency on infrastructure.

The framework's job is to provide the *cross-cutting machinery* that every slice needs (validation,
permissions, tenancy, the unit of work, events, …) so a slice can stay small and intention-revealing.

---

## Layering

Slice keeps the classic DDD dependency direction, but the "application" layer is realised as slices
rather than a service tier:

```
        ┌──────────────────────────────────────────────┐
        │  Host (Program.cs + root SliceModule)         │  composition root
        └──────────────────────────────────────────────┘
                          │ depends on
        ┌──────────────────────────────────────────────┐
        │  Web (controllers / hubs)  +  Feature slices  │  Slice.AspNetCore, your Features/*
        │  (commands, queries, validators, handlers)    │  Slice.Application
        └──────────────────────────────────────────────┘
                          │
        ┌──────────────────────────────────────────────┐
        │  Domain (aggregates, value objects, events,   │  Slice.Domain
        │  repository interfaces)                        │
        └──────────────────────────────────────────────┘
                          ▲ implemented by
        ┌──────────────────────────────────────────────┐
        │  Infrastructure (EF, repositories, outbox,    │  Slice.EntityFrameworkCore, adapters
        │  transports, blob, cache, …)                  │
        └──────────────────────────────────────────────┘

        ┌──────────────────────────────────────────────┐
        │  Core (ambient abstractions, Result, markers) │  Slice.Core — referenced by everything
        └──────────────────────────────────────────────┘
```

The **Domain** depends only on **Core**. Infrastructure depends inward on the Domain and implements
its interfaces (`IRepository<T,TKey>`, `ILeadRepository`). The architecture test suite enforces the
key rules (`Slice.Domain` has no infrastructure dependency; feature slices don't reference each
other; controllers inherit `SliceController`).

---

## Anatomy of a bounded context

A bounded context is a project (e.g. `Slice.Sample.Crm`) with this internal shape:

```
Slice.Sample.Crm/
├─ CrmModule.cs                 # the module: [DependsOn(...)] + ConfigureServices
├─ Domain/
│  └─ Leads/
│     ├─ Lead.cs                # aggregate root (business methods, raises events)
│     ├─ FullName.cs            # value object
│     └─ ILeadRepository.cs     # repository interface (lives with the domain)
├─ Persistence/
│  ├─ CrmDbContext.cs           # : SliceDbContext, owned-type mappings
│  └─ EfLeadRepository.cs       # : EfRepository<…>, ILeadRepository
├─ Permissions/
│  └─ CrmPermissions.cs         # constants + PermissionDefinitionProvider
└─ Features/
   ├─ CreateLead/CreateLead.cs  # command + validator + handler + controller, all in one file
   ├─ ListLeads/ListLeads.cs
   └─ …                         # one folder per use case
```

Adding a use case means adding a folder under `Features/`. The module never changes — handlers,
validators, and permission providers are discovered by assembly scanning (see
[Modularity & DI](modularity-and-di.md)).

---

## The request lifecycle

When a controller calls `SendAsync(command, ct)`, the command travels through the mediator pipeline.
Behaviors run in a fixed order (low number first) regardless of registration order, because each
declares its position via `IHasPipelineOrder`:

```
HTTP request
   │
   ▼
SliceController.SendAsync ──► ISender.SendAsync
   │
   ▼  pipeline behaviors (Slice.Application + cross-cutting modules)
   ├─ 100  LoggingBehavior          logs start/finish/failure
   ├─ 200  MultiTenancyBehavior     ensures the ambient tenant is set
   ├─ 300  AuthorizationBehavior    enforces [SlicePermission] → Result.Forbidden on failure
   ├─ 350  RequiresFeatureBehavior  enforces [RequiresFeature] → Result.Forbidden when off
   ├─ 400  ValidationBehavior       runs FluentValidation → Result.Validation on failure
   ├─ 500  UnitOfWorkBehavior       after the handler succeeds, SaveChangesAsync on every IUnitOfWork
   └─ MAX  the handler itself
   │
   ▼
Handler returns Result<T>
   │
   ▼  UnitOfWorkBehavior commits → SliceAuditingInterceptor stamps audit fields,
   │  DomainEventInterceptor writes integration events to the outbox and dispatches
   │  in-process domain events
   ▼
SliceController maps Result<T> → IActionResult (200/204/400/403/404/409/500 + ProblemDetails)
```

A behavior that returns a failed `Result` short-circuits the rest of the pipeline — e.g. a denied
permission never reaches the handler, and the unit of work never commits. See
[CQRS & the mediator pipeline](cqrs-and-mediator.md) for the details.

---

## Modules and composition

There is no central registration file listing services. Instead:

- Each capability is a **`SliceModule`** that declares its dependencies with `[DependsOn(...)]` and
  registers its services in `ConfigureServices`.
- The host calls `AddSliceModules<TRootModule>()`. The loader walks the dependency graph, **topologically
  sorts** it (Kahn's algorithm, deterministic by type name), and configures each module exactly once,
  dependencies first.
- Most services are registered by **convention**: a class that implements `ITransientDependency`,
  `IScopedDependency`, or `ISingletonDependency` is auto-registered against its interfaces (and against
  abstract base classes that carry a marker, e.g. `PermissionDefinitionProvider`).

This is what lets a slice "just work": when `CrmModule` depends on `SliceAuthorizationModule`, the
authorization behavior, permission checker, and your `PermissionDefinitionProvider` are all wired
without any explicit `services.Add…` in your feature code.

See [Modularity & DI](modularity-and-di.md).

---

## Seams and adapters

Every infrastructural concern is defined as an interface (a *seam*) with a safe default, and optional
adapter packages replace the default:

| Seam | Default | Adapters |
|---|---|---|
| `IDistributedEventPublisher` | local loopback | RabbitMQ, Azure Service Bus, Kafka |
| `IBlobProvider` | FileSystem | AWS S3, Azure Blob, MinIO |
| `IEmailSender` | Null (logs) | SMTP, MailKit |
| `IDistributedCache` (`ISliceCache`) | in-memory | Redis |
| `IDistributedLock` | in-process | Redis |
| `IBackgroundJobManager` | in-memory channel | Hangfire |
| `IPermissionStore` | configuration | claims (auth) → DB grants (management) |
| `ISettingValueProvider` / `IFeatureStore` | configuration | management (DB-backed) |
| `ITenantConnectionStore` | none (shared DB) | in-memory map → your own (DB-per-tenant) |

Adapters register with `services.RemoveAll<TSeam>()` followed by `services.Add…<TSeam, TImpl>()`, so
**the last module configured wins**. Because modules are configured in dependency order, a module
that depends on another can deliberately override it — this is how `Slice.Management`'s DB-backed
permission store supersedes `Slice.Authentication`'s claims store.

---

## ABP parity

Slice deliberately mirrors ABP's building blocks so the mental model transfers, while diverging where
VSA or pragmatism suggests. Notable equivalences and divergences:

| ABP | Slice | Note |
|---|---|---|
| ABP modules + `[DependsOn]` | `SliceModule` + `[DependsOn]` | Same topological loading |
| `ITransientDependency` etc. | identical marker interfaces | Same convention DI |
| `IRepository<T,TKey>` | identical | `GetListAsync`, `InsertAsync(autoSave)`, … |
| Unit of work interceptor | `UnitOfWorkBehavior` + `IUnitOfWork` on `SliceDbContext` | `autoSave:false` in handlers |
| Audit logging | `SliceAuditingInterceptor` | Creator/Modifier/soft-delete stamping |
| Distributed event bus + outbox | `Slice.EventBus` + EF outbox | Transport seam |
| Permission management | `Slice.Authorization` + `Slice.Management` | DB grants replace claims store |
| Setting / feature management | `Slice.Settings` / `Slice.Features` + `Slice.Management` | Layered providers |
| Five `*Management` modules | **one** `Slice.Management` package | Consolidated, documented divergence |
| `Volo.Abp.Auditing` | *(built into the EF interceptor)* | not a separate package |
| `Volo.Abp.FluentValidation` | *(built into `ValidationBehavior`)* | FluentValidation is the default |
| `Volo.Abp.Timing` | `IClock` in `Slice.Core` | UTC clock abstraction |
| `Volo.Abp.Mapperly` | `Slice.ObjectMapping` | `IObjectMapper<,>` accepts Mapperly-generated mappers |

---

## Design principles

1. **A feature is a folder.** Co-locate everything one use case needs; let it change as a unit.
2. **The domain is pure.** Aggregates enforce invariants; infrastructure depends inward.
3. **Cross-cutting is a pipeline, not boilerplate.** Validation, permissions, tenancy, and the unit
   of work are behaviors — slices don't repeat them.
4. **Every infrastructure choice is a swappable seam** with a zero-config default.
5. **Composition is declarative.** Modules + `[DependsOn]` + convention DI; no god registration file.
6. **Results over exceptions for expected failures.** `Result<T>` carries validation/permission/not-found
   outcomes; exceptions remain for the truly exceptional and are mapped to ProblemDetails centrally.
7. **Multi-tenant and auditable by default.** `IMultiTenant` + `ISoftDelete` global filters and audit
   stamping apply automatically to entities that opt in.
