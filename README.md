# Slice — a .NET 10 application framework

**Slice** is an opinionated, modular .NET 10 application framework that combines **Vertical Slice
Architecture (VSA)** with **Domain-Driven Design (DDD)**. It is an ABP-style alternative with the same
breadth of building blocks — modules and convention-based DI, a pluggable mediator pipeline, DDD
aggregates with EF persistence and a unit of work, multi-tenancy, authentication, dynamic permissions,
settings, features, localization, background jobs, a transactional outbox, distributed events, caching,
blob storage, email, distributed locking, object mapping, API versioning and more — organized on one
principle:

> **A feature is a folder, not a layer.** Everything one use case needs (its command/query, validation,
> handler, and HTTP endpoint) lives together and is auto-discovered. Cross-cutting concerns are wired
> once by modules; the slice only states intent.

Business outcomes travel as a `Result`/`Error` (mapped to HTTP status codes); only programming/infra
faults throw. Infrastructure lives behind seams, and concrete providers (Redis, RabbitMQ, Kafka,
Postgres, S3, …) are **opt-in adapter packages** you add only when you need them.

> **Status — feature-complete.** Modules + DI, the mediator pipeline (custom or MediatR), EF
> persistence with auditing/soft-delete/domain-events/outbox, multi-tenancy (row-level **and**
> database-per-tenant), OpenIddict auth with DB-backed dynamic permissions, settings/features/
> localization, messaging (RabbitMQ / Azure Service Bus / Kafka / Postgres), caching, blob storing,
> email, background jobs + workers, distributed locking, object mapping, API versioning, a full
> **PostgreSQL stack** (run everything on one Postgres, incl. pgvector search), `dotnet new` templates,
> and a test suite (unit + architecture + real-broker Testcontainers) — all exercised by five runnable
> samples.

---

## Contents

- [The 30-second picture](#the-30-second-picture)
- [Quick start](#quick-start)
- [Anatomy of a vertical slice](#anatomy-of-a-vertical-slice)
- [How it works](#how-it-works)
  - [Modules & dependency injection](#modules--dependency-injection)
  - [The mediator pipeline](#the-mediator-pipeline)
  - [Domain & persistence](#domain--persistence)
  - [Web API & results](#web-api--results)
  - [Multi-tenancy](#multi-tenancy)
  - [Security: authentication, authorization & management](#security-authentication-authorization--management)
  - [Messaging & events](#messaging--events)
  - [Cross-cutting services](#cross-cutting-services)
  - [The PostgreSQL stack](#the-postgresql-stack)
  - [Seams & adapters](#seams--adapters)
- [Scaffolding from templates](#scaffolding-from-templates)
- [Package catalog](#package-catalog)
- [Samples](#samples)
- [Testing](#testing)
- [Configuration](#configuration)
- [Documentation map](#documentation-map)

---

## The 30-second picture

```csharp
// Program.cs — compose the app from one root module's dependency graph
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSliceModules<AppModule>(builder.Configuration);
builder.Services.AddSliceApiVersioning();
builder.Services.AddSliceOpenApi();                 // OpenAPI doc + Scalar UI

var app = builder.Build();
await app.Services.InitializeSliceModulesAsync();   // EnsureCreated, seeding, …

app.UseSliceExceptionHandling();
app.UseSliceLocalization();
app.UseSliceAuthentication();
app.UseSliceMultiTenancy();
app.MapControllers();
app.MapSliceOpenApi();                              // /openapi/v1.json + /scalar/v1
app.Run();
```

```csharp
// Features/CreateLead/CreateLead.cs — one self-contained vertical slice
[SlicePermission(CrmPermissions.Leads.Create)]
public sealed record CreateLeadCommand(string FirstName, string LastName, string? Email)
    : ICommand<Result<Guid>>;

public sealed class CreateLeadValidator : AbstractValidator<CreateLeadCommand>
{
    public CreateLeadValidator() => RuleFor(x => x.FirstName).NotEmpty();
}

public sealed class CreateLeadHandler(ILeadRepository repository, IGuidGenerator guids, ICurrentTenant tenant)
    : ICommandHandler<CreateLeadCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(CreateLeadCommand command, CancellationToken ct)
    {
        var lead = new Lead(guids.Create(), tenant.Id, FullName.Create(command.FirstName, command.LastName));
        await repository.InsertAsync(lead, autoSave: false, ct);   // UoW behavior commits on success
        return Result<Guid>.Success(lead.Id);
    }
}

[Authorize]
[Route("api/crm/leads")]
public sealed class CreateLeadController : SliceController
{
    [HttpPost]
    public Task<IActionResult> Create([FromBody] CreateLeadCommand command, CancellationToken ct)
        => SendAsync(command, ct);   // routes through the mediator pipeline; Result<T> → HTTP
}
```

Everything that request touches — validation, the permission check, multi-tenant filtering, the unit of
work that commits and fires the audit + domain-event interceptors — is wired by the modules in
`AppModule`'s dependency graph. **Adding the slice requires no central wiring**: the handler, validator,
and repository are discovered by the module's assembly scan.

---

## Quick start

**Prerequisites:** the **.NET 10 SDK**. Docker is optional (only the real-broker integration tests need
it).

```bash
dotnet build Slice.slnx
dotnet run --project samples/Slice.Sample.Crm     # listens on http://localhost:5273 (https 7181)
```

Browse the interactive API docs at **http://localhost:5273/scalar/v1** (OpenAPI JSON at
`/openapi/v1.json`). Then get a token for the seeded demo admin and call the API:

```bash
# 1) get an access token (password grant)
curl -X POST http://localhost:5273/connect/token \
  --data-urlencode grant_type=password --data-urlencode username=admin@slice \
  --data-urlencode password=Admin123! --data-urlencode 'scope=api offline_access'

# 2) with Authorization: Bearer <access_token>:
# GET  /api/crm/leads        -> list (tenant-filtered)        | no token         -> 401
# POST /api/crm/leads        -> create (returns id)           | missing permission -> 403
# GET  /api/crm/leads/{id}   -> fetch (LeadDto, audited)      | invalid body      -> 400
```

The sample uses SQLite (`Data Source=crm.db`, auto-created on startup). New to the framework? Read
[**Getting started**](docs/getting-started.md) next.

---

## Anatomy of a vertical slice

A use case is a folder under `Features/<Name>/`. The `CreateLead` slice above colocates four things:

| Part | Type | Role |
|---|---|---|
| Command/query | `record : ICommand<Result<T>>` / `IQuery<…>` | the intent; carries `[SlicePermission]` / `[RequiresFeature]` metadata |
| Validator | `AbstractValidator<T>` (FluentValidation) | structural rules; runs in the pipeline → `400` |
| Handler | `ICommandHandler<,>` / `IQueryHandler<,>` | the behavior; returns `Result<T>` |
| Endpoint | `SliceController` (or `ISliceEndpoint` minimal API) | thin; just calls `SendAsync(command)` |

The request flows through a deterministic pipeline:

```
Controller → ISender → [ Logging → MultiTenancy → Authorization → FeatureCheck → Validation → UnitOfWork ] → Handler → Result → HTTP
```

Handlers, validators, and repositories are auto-discovered by the owning module's assembly scan, so a
new slice needs **no edits to any central registration**. See [`samples/Slice.Sample.Crm`](samples/Slice.Sample.Crm)
for the canonical bounded context.

---

## How it works

### Modules & dependency injection

A package or bounded context is a `SliceModule` that declares its dependencies with
`[DependsOn(typeof(...))]`. Modules are configured in **topological order** and bootstrapped with
`AddSliceModules<TRoot>(config)` + `await InitializeSliceModulesAsync()`.

```csharp
[DependsOn(typeof(SliceAspNetCoreModule), typeof(SliceEntityFrameworkCoreModule))]
public sealed class CrmModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context) =>
        context.Services.AddSliceDbContext<CrmDbContext>(o => o.UseSqlite("Data Source=crm.db"));
}
```

**Lifecycle hooks** run in this order across the whole module graph:

| Hook | When | Typical use |
|---|---|---|
| `PreConfigureServices` | before any `ConfigureServices` | set options other modules read |
| `ConfigureServices` | register services | the bulk of wiring |
| `PostConfigureServices` | after all `ConfigureServices` | finalize / override |
| `OnApplicationInitializationAsync` | after the container is built | migrations, seeding |
| `OnApplicationShutdownAsync` | on shutdown | flush / cleanup |

**Convention DI** — a service is registered just by implementing a marker interface; the module's
`ConventionalRegistrar` discovers it (alongside handlers, validators, and repositories):

| Marker | Lifetime |
|---|---|
| `ITransientDependency` | Transient |
| `IScopedDependency` | Scoped |
| `ISingletonDependency` | Singleton |

→ Deep dive: [**Modularity & DI**](docs/modularity-and-di.md)

### The mediator pipeline

The mediator is an abstraction (`ISender`, `IRequest<T>`, `IRequestHandler<,>`, `IPipelineBehavior<,>`)
with two interchangeable engines: the built-in [`Slice.Mediator.Default`](src/Slice.Mediator.Default/README.md)
and a [MediatR adapter](src/Slice.Mediator.MediatR/README.md) (`AddSliceMediatorMediatR`). Conformance
tests assert both produce identical results **and** identical behavior order.

Behavior order is **deterministic** — independent of cross-module registration order — via
`IHasPipelineOrder` and the constants in [`src/Slice.Mediator/PipelineOrder.cs`](src/Slice.Mediator/PipelineOrder.cs):

| Order | Constant | Behavior | Responsibility |
|---:|---|---|---|
| 100 | `Logging` | `LoggingBehavior` | log start / success / failure |
| 200 | `MultiTenancy` | tenancy behavior | ensure the ambient tenant |
| 300 | `Authorization` | `AuthorizationBehavior` | enforce `[SlicePermission]` → 403 |
| 350 | `FeatureCheck` | `RequiresFeatureBehavior` | enforce `[RequiresFeature]` → 403 |
| 400 | `Validation` | `ValidationBehavior` | FluentValidation → 400 |
| 500 | `UnitOfWork` | `UnitOfWorkBehavior` | `SaveChangesAsync` on success |
| `int.MaxValue` | `Default` | your behaviors | run innermost |

→ Deep dive: [**CQRS & the mediator pipeline**](docs/cqrs-and-mediator.md)

### Domain & persistence

[`Slice.Domain`](src/Slice.Domain/README.md) supplies the DDD building blocks — `Entity`,
`AggregateRoot`, `ValueObject`, audited bases (`CreationAuditedAggregateRoot`, `FullAuditedAggregateRoot`),
`IMultiTenant`, `IHasExtraProperties` (schema-less JSON column), `IRepository<T>`, specifications, and
`Ensure` guards. Aggregates raise domain events; entities can carry extra properties without a schema
change.

[`Slice.EntityFrameworkCore`](src/Slice.EntityFrameworkCore/README.md) provides `SliceDbContext`,
`EfRepository`, and interceptors for auditing, soft-delete, and domain-event dispatch, plus global query
filters (soft-delete + multi-tenant) and a **transactional outbox**. The key rule:

> Handlers write with **`autoSave: false`**; the `UnitOfWorkBehavior` commits after the command
> succeeds, which is what triggers the auditing and domain-event interceptors. **Don't call
> `SaveChanges` inside a handler.**

The same DbContext connection/transaction is shared by alternative ORMs — [`Slice.Dapper`](src/Slice.Dapper/README.md)
and [`Slice.LinqToDB`](src/Slice.LinqToDB/README.md) — so all three see each other's writes and roll
back together.

→ Deep dive: [**Domain & persistence**](docs/domain-and-persistence.md)

### Web API & results

`SliceController.SendAsync` sends a request through the pipeline and maps the `Result<T>` to HTTP;
failures become RFC-7807 ProblemDetails. The status mapping:

| Outcome | HTTP |
|---|---|
| Success (no value) | 204 No Content |
| Success (with value) | 200 OK |
| Validation | 400 (ValidationProblemDetails) |
| Unauthorized | 401 |
| Forbidden | 403 |
| NotFound | 404 |
| Conflict | 409 |
| Failure / other | 500 |

Recommended middleware order: `UseSliceExceptionHandling → UseSliceLocalization → UseSliceAuthentication
→ UseSliceMultiTenancy`. Minimal APIs are first-class and reach parity with controllers: define an
`ISliceEndpoint`, map them with `MapSliceEndpoints`, and they dispatch through the **same** handlers and
pipeline. HAL hypermedia (`_links`/`_embedded`, content-negotiated and permission-aware) and conditional
requests (`ETag`/`304`, `If-Match`/`412`) are opt-in filters.

→ Deep dives: [**Web API & results**](docs/web-and-results.md) ·
[**Minimal APIs**](docs/minimal-apis.md) · [**Hypermedia & HTTP caching**](docs/hypermedia-and-caching.md)

### Multi-tenancy

`ICurrentTenant` is an ambient (AsyncLocal) value resolved per request by a contributor chain — a
`tenant_id` claim or the `X-Tenant-Id` header — set up by `UseSliceMultiTenancy`. Two isolation models
are supported:

- **Row-level (shared database):** an `IMultiTenant` global query filter scopes every read to the
  current tenant automatically.
- **Database-per-tenant:** `AddSliceMultiTenantDbContext` resolves the connection string per scope from
  `ITenantConnectionStore`/`ITenantConnectionResolver`.

→ Deep dive: [**Multi-tenancy**](docs/multitenancy.md)

### Security: authentication, authorization & management

[`Slice.Authentication`](src/Slice.Authentication/README.md) is an OpenIddict OAuth2/OIDC server with
ASP.NET Identity (Guid keys), `/connect/token` (password + refresh grants, JWT access tokens), an
`HttpCurrentUser`, and a seeded demo admin. [`Slice.Authorization`](src/Slice.Authorization/README.md)
adds permission definitions, `[SlicePermission]`, and the authorization behavior.

Permissions are checked against an `IPermissionStore`, layered:

1. **`ConfigurationPermissionStore`** — `Authorization:GrantedPermissions` from configuration.
2. **`ClaimsPermissionStore`** — `permission` claims on the JWT (changes require re-login).
3. **`PermissionManagementStore`** ([`Slice.Management`](src/Slice.Management/README.md)) — **DB-backed
   grants per user/role, effective immediately on the same token**.

`Slice.Management` also adds tenant, setting, and feature management plus admin controllers
(`api/management/permissions|tenants|identity`) and seeds every declared permission to the `admin` role.

→ Deep dives: [**Security**](docs/security.md) · [**Permissions walkthrough**](docs/permissions.md)

### Messaging & events

Aggregates raise events; two kinds flow out:

- **Domain events** (`IDomainEvent`) — in-process, handled within the same transaction.
- **Distributed/integration events** (`IDistributedEvent`) — written to the **transactional outbox** in
  the same transaction as the data change, then delivered at-least-once by the `OutboxProcessor` (which
  takes a distributed lock for single-runner). Receivers dedup via an `IInboxStore`.

Transports plug into the `IDistributedEventPublisher` seam: [RabbitMQ](src/Slice.EventBus.RabbitMQ/README.md),
[Azure Service Bus](src/Slice.EventBus.AzureServiceBus/README.md), [Kafka](src/Slice.EventBus.Kafka/README.md),
or [Postgres](src/Slice.EventBus.Postgres/README.md) (LISTEN/NOTIFY).

→ Deep dive: [**Messaging & events**](docs/messaging.md)

### Cross-cutting services

Each is an interface + a default, with opt-in adapter packages behind the seam:

| Service | Package | Default | Adapters / notes |
|---|---|---|---|
| Settings | [`Slice.Settings`](src/Slice.Settings/README.md) | config + definition default | precedence: management DB (−10) → global (0) → config (10) → default (100) |
| Features | [`Slice.Features`](src/Slice.Features/README.md) | config store | `[RequiresFeature]` gate |
| Localization | [`Slice.Localization`](src/Slice.Localization/README.md) | contributor dictionaries | `ISliceLocalizer`, request-localization |
| Caching | [`Slice.Caching`](src/Slice.Caching/README.md) | in-memory | [Redis](src/Slice.Caching.Redis/README.md); tenant-isolated keys |
| Blob storing | [`Slice.BlobStoring`](src/Slice.BlobStoring/README.md) | FileSystem | [S3](src/Slice.BlobStoring.Aws/README.md), [Azure](src/Slice.BlobStoring.Azure/README.md), [MinIO](src/Slice.BlobStoring.Minio/README.md) |
| Email | [`Slice.Emailing`](src/Slice.Emailing/README.md) | Null + SMTP | [MailKit](src/Slice.Emailing.MailKit/README.md) |
| Background jobs | [`Slice.BackgroundJobs`](src/Slice.BackgroundJobs/README.md) | in-memory worker | [Hangfire](src/Slice.BackgroundJobs.Hangfire/README.md) |
| Background workers | [`Slice.BackgroundWorkers`](src/Slice.BackgroundWorkers/README.md) | periodic `IBackgroundWorker` | — |
| Distributed locking | [`Slice.DistributedLocking`](src/Slice.DistributedLocking/README.md) | in-process | [Redis](src/Slice.DistributedLocking.Redis/README.md) |
| Object mapping | [`Slice.ObjectMapping`](src/Slice.ObjectMapping/README.md) | typed `IObjectMapper` | — |
| API versioning | [`Slice.ApiVersioning`](src/Slice.ApiVersioning/README.md) | wraps Asp.Versioning | — |
| Virtual file system | [`Slice.VirtualFileSystem`](src/Slice.VirtualFileSystem/README.md) | embedded + physical composite | — |
| Logging | [`Slice.Serilog`](src/Slice.Serilog/README.md) | — | `UseSliceSerilog` + per-request tenant/user enrichment |
| Real-time | [`Slice.AspNetCore.SignalR`](src/Slice.AspNetCore.SignalR/README.md) | — | tenant/user-aware `SliceHub` |

→ Deep dive: [**Cross-cutting services**](docs/cross-cutting-services.md)

### The PostgreSQL stack

One Postgres can back the **entire** application. `AddSlicePostgresStack` + `UseSlicePostgres` wire a
shared `NpgsqlDataSource` pool across every seam:

| Concern | Seam | Storage |
|---|---|---|
| Cache | `IDistributedCache` | `slice_cache` |
| Lock | `IDistributedLock` | advisory locks |
| Event bus | `IDistributedEventPublisher` | `slice_event_queue` (LISTEN/NOTIFY + SKIP LOCKED) |
| Jobs | `IBackgroundJobManager` | `slice_jobs` |
| Blob storage | `IBlobProvider` | `slice_blobs` (`bytea`) |
| Vector search | `IVectorStore` | pgvector |
| EF data | EF Core | your tables + `SliceOutbox`/`SliceInbox` |

Vector search (`Slice.Vector` + `Slice.Vector.Postgres`) pairs with an `IEmbeddingGenerator`
(`HashingEmbeddingGenerator` for dev, [OpenAI-compatible](src/Slice.Embeddings.OpenAI/README.md) for
prod). Scaffold one with `dotnet new slice-api --database postgres`.

The "stack" is optional: each row above is an independent adapter on the shared `Slice.Postgres` data
source (its own `AddSlicePostgresXxx()`), so you can put just **one** seam on Postgres and keep the rest
elsewhere. `AddSlicePostgresStack` is simply the one-call aggregator of those registrations.

→ Deep dive: [**The PostgreSQL stack**](docs/postgresql-stack.md)

### Seams & adapters

The opt-in design rests on a small set of seams; you swap an implementation with `RemoveAll<T>()` + an
adapter's `Add…`:

| Seam | Default | Adapters |
|---|---|---|
| `IDistributedEventPublisher` | local loopback | RabbitMQ, Azure SB, Kafka, Postgres |
| `IBlobProvider` | FileSystem | S3, Azure, MinIO, Postgres |
| `IEmailSender` | Null / SMTP | MailKit |
| `IDistributedCache` | in-memory | Redis, Postgres |
| `IDistributedLock` | in-process | Redis, Postgres |
| `IBackgroundJobManager` | in-memory | Hangfire, Postgres |
| `IPermissionStore` | config | claims (auth) → DB (management) |
| Mediator engine | Default | MediatR |

→ Deep dive: [**Architecture**](docs/architecture.md)

---

## Scaffolding from templates

[`Slice.Templates`](templates) ships seven `dotnet new` templates:

| Short name | Produces |
|---|---|
| **`slice-api`** | full controller host (auth + management + EF); `--database sqlite\|postgres` |
| **`slice-api-minimal`** | controller-free minimal-API host (`ISliceEndpoint`, HAL/ETag/OpenAPI) |
| **`slice-module`** | a bounded-context classlib (module + DbContext + aggregate + feature) |
| **`slice-monolith`** | a modular-monolith **solution** (Host + Orders + Billing + Contracts, in-process events) |
| **`slice-worker`** | a headless worker host (no web) with a periodic `IBackgroundWorker` |
| **`slice-tenant-api`** | a database-per-tenant API (tenant registry/onboarding); `--migrations host` (default, in-host) or `job` (adds a separate migration-job project) |
| **`slice-feature`** | a single vertical slice (item template) to drop into a module |

```bash
# one-time: build packages + the template package, install the templates
dotnet pack Slice.slnx -c Release -o artifacts
dotnet pack templates/Slice.Templates.csproj -o artifacts
dotnet new install ./artifacts/Slice.Templates.0.1.0.nupkg

# scaffold and run a new application (point restore at the local feed until Slice.* is on nuget.org)
dotnet new slice-api -n Acme.Shop -o Acme.Shop
cd Acme.Shop
dotnet nuget add source ../artifacts -n slice-local --configfile nuget.config
dotnet run            # admin@slice / Admin123! ; POST /connect/token then call /api/notes

# other starters
dotnet new slice-api-minimal -n Acme.Api -o Acme.Api
dotnet new slice-monolith    -n Acme     -o Acme
dotnet new slice-worker      -n Acme.Jobs -o Acme.Jobs
dotnet new slice-tenant-api  -n Acme.Tenants -o Acme.Tenants

# add a bounded context, then a slice inside it
dotnet new slice-module -n Billing -o Billing
dotnet new slice-feature -n ArchiveNote --module Acme.Shop
```

→ More detail: [**Getting started**](docs/getting-started.md)

---

## Package catalog

Every package targets **net10.0**, is version **0.1.0**, and uses central package management. Adapters
(`*.Redis`, `*.Aws`, `*.RabbitMQ`, …) are optional and swap an implementation behind a seam. The full
annotated reference with summaries is in [**docs/README.md → Package reference**](docs/README.md#package-reference).

**Foundation** — [Core](src/Slice.Core/README.md) · [Domain](src/Slice.Domain/README.md) ·
[Modularity](src/Slice.Modularity/README.md)

**Application & mediator** — [Mediator](src/Slice.Mediator/README.md) ·
[Mediator.Default](src/Slice.Mediator.Default/README.md) ·
[Mediator.MediatR](src/Slice.Mediator.MediatR/README.md) · [Application](src/Slice.Application/README.md)

**Web** — [AspNetCore](src/Slice.AspNetCore/README.md) · [ApiVersioning](src/Slice.ApiVersioning/README.md) ·
[AspNetCore.SignalR](src/Slice.AspNetCore.SignalR/README.md) ·
[AspNetCore.Hypermedia](src/Slice.AspNetCore.Hypermedia/README.md) ·
[AspNetCore.ConditionalRequests](src/Slice.AspNetCore.ConditionalRequests/README.md) ·
[AspNetCore.MinimalApi](src/Slice.AspNetCore.MinimalApi/README.md)

**Persistence** — [EntityFrameworkCore](src/Slice.EntityFrameworkCore/README.md) ·
[Dapper](src/Slice.Dapper/README.md) · [LinqToDB](src/Slice.LinqToDB/README.md)

**Multi-tenancy, security & management** — [MultiTenancy](src/Slice.MultiTenancy/README.md) ·
[Authorization](src/Slice.Authorization/README.md) · [Authentication](src/Slice.Authentication/README.md) ·
[Management](src/Slice.Management/README.md)

**Messaging** — [EventBus](src/Slice.EventBus/README.md) ·
[EventBus.RabbitMQ](src/Slice.EventBus.RabbitMQ/README.md) ·
[EventBus.AzureServiceBus](src/Slice.EventBus.AzureServiceBus/README.md) ·
[Kafka](src/Slice.Kafka/README.md) · [EventBus.Kafka](src/Slice.EventBus.Kafka/README.md)

**Cross-cutting services** — [Settings](src/Slice.Settings/README.md) · [Features](src/Slice.Features/README.md) ·
[Localization](src/Slice.Localization/README.md) · [Caching](src/Slice.Caching/README.md) ·
[Caching.Redis](src/Slice.Caching.Redis/README.md) · [BlobStoring](src/Slice.BlobStoring/README.md) ·
[BlobStoring.Aws](src/Slice.BlobStoring.Aws/README.md) · [BlobStoring.Azure](src/Slice.BlobStoring.Azure/README.md) ·
[BlobStoring.Minio](src/Slice.BlobStoring.Minio/README.md) · [Emailing](src/Slice.Emailing/README.md) ·
[Emailing.MailKit](src/Slice.Emailing.MailKit/README.md) · [BackgroundJobs](src/Slice.BackgroundJobs/README.md) ·
[BackgroundJobs.Hangfire](src/Slice.BackgroundJobs.Hangfire/README.md) ·
[BackgroundWorkers](src/Slice.BackgroundWorkers/README.md) ·
[DistributedLocking](src/Slice.DistributedLocking/README.md) ·
[DistributedLocking.Redis](src/Slice.DistributedLocking.Redis/README.md) ·
[ObjectMapping](src/Slice.ObjectMapping/README.md) · [VirtualFileSystem](src/Slice.VirtualFileSystem/README.md) ·
[Serilog](src/Slice.Serilog/README.md)

**PostgreSQL adapters** (foundation `Slice.Postgres` + per-seam adapters, each opt-in on its own;
`Slice.PostgresStack` just aggregates them) — [Postgres](src/Slice.Postgres/README.md) · [PostgresStack](src/Slice.PostgresStack/README.md) ·
[EntityFrameworkCore.PostgreSQL](src/Slice.EntityFrameworkCore.PostgreSQL/README.md) ·
[Caching.Postgres](src/Slice.Caching.Postgres/README.md) ·
[DistributedLocking.Postgres](src/Slice.DistributedLocking.Postgres/README.md) ·
[EventBus.Postgres](src/Slice.EventBus.Postgres/README.md) ·
[BackgroundJobs.Postgres](src/Slice.BackgroundJobs.Postgres/README.md) ·
[BlobStoring.Postgres](src/Slice.BlobStoring.Postgres/README.md) · [Vector](src/Slice.Vector/README.md) ·
[Vector.Postgres](src/Slice.Vector.Postgres/README.md) · [Embeddings.OpenAI](src/Slice.Embeddings.OpenAI/README.md)

---

## Samples

Under [`samples/`](samples) — these are the framework's living integration tests of the real wiring:

| Sample | What it proves | Run |
|---|---|---|
| [Slice.Sample.Crm](samples/Slice.Sample.Crm) | The full feature set: a Leads bounded context with auth, dynamic permissions, multi-tenancy, EF persistence, domain/distributed events, jobs, HAL, ETag, Serilog, SignalR, OpenAPI/Scalar — controllers **and** minimal APIs sharing one pipeline | `dotnet run --project samples/Slice.Sample.Crm` |
| [Slice.Sample.MinimalApi](samples/Slice.Sample.MinimalApi) | A controller-free host: `ISliceEndpoint`, versioned groups, HAL/ETag parity | `dotnet run --project samples/Slice.Sample.MinimalApi` |
| [Slice.Sample.PostgresStack](samples/Slice.Sample.PostgresStack) | Everything on one Postgres: cache, lock, event bus, jobs, blobs, pgvector search + embeddings, EF data | `dotnet run --project samples/Slice.Sample.PostgresStack` |
| [Slice.Sample.MultiTenant](samples/Slice.Sample.MultiTenant) | Database-per-tenant: physical DB isolation, runtime tenant onboarding, `X-Tenant-Id` resolution | `dotnet run --project samples/Slice.Sample.MultiTenant` |
| [modular-monolith](samples/modular-monolith) | Four modules over three ORMs (EF / LinqToDB / Dapper) choreographed by in-process distributed events + SignalR | `dotnet run --project samples/modular-monolith/Slice.Sample.Monolith.Host` |

---

## Testing

```bash
# fast subset (no Docker)
dotnet test Slice.slnx \
  --filter "FullyQualifiedName!~RabbitMQ&FullyQualifiedName!~Kafka&FullyQualifiedName!~Minio&FullyQualifiedName!~MailKit"

# everything (Docker required for the broker/store tests)
dotnet test Slice.slnx
```

The suite mixes fast in-process tests (domain, mediator conformance, architecture rules via NetArchTest,
shared-connection data tests) with **Testcontainers** integration tests against real RabbitMQ, Kafka,
MinIO, and Mailpit SMTP. Architecture tests enforce that Domain has no infrastructure dependencies,
slices don't reference each other, and controllers inherit `SliceController`.

→ Deep dive: [**Testing**](docs/testing.md)

---

## Configuration

Configuration is read from standard `appsettings.json` / environment variables. Examples:

```jsonc
{
  "ConnectionStrings": { "Default": "Data Source=crm.db" },
  "SliceAuth": { "SeedDemoAdmin": true, "AdminEmail": "admin@slice", "AdminPassword": "Admin123!" },
  "Authorization": { "GrantedPermissions": [ "Crm.Leads.View" ] },
  "Settings": { "Crm.MaxLeadsPerDay": "45" },
  "Features": { "Crm.Export": "true" }
}
```

Every key the framework reads — auth, authorization, settings, features, blob storage, logging — plus
the options configured in code (Redis, SMTP, RabbitMQ, Kafka, S3, …) is documented in the
[**Configuration reference**](docs/configuration-reference.md).

---

## Documentation map

The conceptual docs in [`docs/`](docs/README.md), in reading order:

| # | Page | Covers |
|---|---|---|
| 1 | [Getting started](docs/getting-started.md) | Install, templates, build & run, first slice |
| 2 | [Architecture](docs/architecture.md) | VSA + DDD, layering, the dependency graph, ABP comparison |
| 3 | [Modularity & DI](docs/modularity-and-di.md) | `SliceModule`, `[DependsOn]`, topological loader, conventions |
| 4 | [CQRS & the mediator pipeline](docs/cqrs-and-mediator.md) | Commands/queries, behaviors, ordering, MediatR |
| 5 | [Domain & persistence](docs/domain-and-persistence.md) | Aggregates, repositories, EF, UoW, auditing, outbox, Dapper, LinqToDB |
| 6 | [Multi-tenancy](docs/multitenancy.md) | Tenant resolution, row-level, database-per-tenant |
| 7 | [Security](docs/security.md) | OpenIddict, ASP.NET Identity, permissions, management |
| 7b | [Permissions walkthrough](docs/permissions.md) | Define → require → check → assign (runnable 403→grant→200) |
| 8 | [Messaging & events](docs/messaging.md) | Domain/distributed events, transports, the outbox |
| 9 | [Cross-cutting services](docs/cross-cutting-services.md) | Settings, features, localization, caching, blob, email, jobs, … |
| 9b | [PostgreSQL stack](docs/postgresql-stack.md) | Run everything on Postgres |
| 10 | [Web API & results](docs/web-and-results.md) | Controllers, `Result<T>`→HTTP, ProblemDetails |
| 10b | [Hypermedia & HTTP caching](docs/hypermedia-and-caching.md) | HAL, `ETag`/304, `If-Match`/412 |
| 10c | [Minimal APIs](docs/minimal-apis.md) | `ISliceEndpoint`, parity with controllers, OpenAPI |
| 11 | [Configuration reference](docs/configuration-reference.md) | Every configuration key |
| 12 | [Testing](docs/testing.md) | The suite, Testcontainers patterns, testing a slice |
