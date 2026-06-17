# Slice Framework — Documentation

**Slice** is a .NET 10 application framework that combines **Vertical Slice Architecture (VSA)** with
**Domain-Driven Design (DDD)**. It is an opinionated, modular alternative to ABP: same breadth of
building blocks (modularity, a mediator pipeline, EF persistence with unit-of-work, multi-tenancy,
permissions, settings, features, localization, background jobs, an outbox, distributed events, blob
storage, caching, email, distributed locking, object mapping, API versioning, a virtual file system,
authentication/authorization and management) — organised so that **a feature is a folder, not a layer**.

This folder is the conceptual documentation. Each library also ships its own `README.md` with the
exact API surface (linked from the [Package reference](#package-reference) below).

---

## Read these in order

| # | Page | What it covers |
|---|------|----------------|
| 1 | [Getting started](getting-started.md) | Install, `dotnet new` templates, build & run the sample, first vertical slice |
| 2 | [Architecture](architecture.md) | VSA + DDD principles, the layering, the dependency graph, ABP comparison |
| 3 | [Modularity & DI](modularity-and-di.md) | `SliceModule`, `[DependsOn]`, the topological loader, convention-based registration |
| 4 | [CQRS & the mediator pipeline](cqrs-and-mediator.md) | `ISender`, commands/queries, pipeline behaviors, ordering, the MediatR adapter |
| 5 | [Domain & persistence](domain-and-persistence.md) | Aggregates, value objects, repositories, EF, unit of work, auditing, outbox/inbox, Dapper, LinqToDB |
| 6 | [Multi-tenancy](multitenancy.md) | Tenant resolution, row-level isolation, **database-per-tenant** |
| 7 | [Security](security.md) | OpenIddict authentication, ASP.NET Identity, permissions, the management module |
| 7b | [Permissions walkthrough](permissions.md) | Define → require → check → assign, end-to-end with a runnable `403 → grant → 200` flow |
| 8 | [Messaging & events](messaging.md) | Local domain events, distributed events, transports (RabbitMQ / Azure SB / Kafka), the outbox |
| 9 | [Cross-cutting services](cross-cutting-services.md) | Settings, features, localization, caching, blob storage, email, jobs, workers, locking, mapping, API versioning, VFS, Serilog, SignalR |
| 9b | [PostgreSQL stack](postgresql-stack.md) | Run **everything on Postgres** — cache, lock, event bus, jobs, blob, vector + embeddings, EF data |
| 10 | [Web API & results](web-and-results.md) | Controllers, `Result<T>` → HTTP mapping, ProblemDetails, exception handling |
| 10b | [Hypermedia & HTTP caching](hypermedia-and-caching.md) | HAL `_links`/`_embedded` (content-negotiated, permission-aware), `ETag`/`304`, `If-Match`/`412` |
| 10c | [Minimal APIs](minimal-apis.md) | `ISliceEndpoint` + `MapSliceEndpoints`, `Result`→HTTP mapping, HAL/ETag parity, versioned groups, OpenAPI, coexistence with controllers |
| 11 | [Configuration reference](configuration-reference.md) | Every configuration key the framework reads |
| 12 | [Testing](testing.md) | The test suite, Testcontainers patterns, how to test a slice |

---

## The 30-second picture

```csharp
// Program.cs — compose the app from one root module's dependency graph
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSliceModules<AppModule>(builder.Configuration);
builder.Services.AddSliceApiVersioning();

var app = builder.Build();
await app.Services.InitializeSliceModulesAsync();   // EnsureCreated, seeding, …

app.UseSliceExceptionHandling();
app.UseSliceLocalization();
app.UseSliceAuthentication();
app.UseSliceMultiTenancy();
app.MapControllers();
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

Everything that request touches — validation, the permission check, multi-tenant filtering, the unit
of work that commits and fires the audit + domain-event interceptors — is wired by the modules in
`AppModule`'s dependency graph. The slice itself only states intent.

---

## Package reference

Every package targets **net10.0**, is version **0.1.0**, and is published with central package
management. Adapters (`*.Redis`, `*.Aws`, `*.RabbitMQ`, …) are optional and swap an implementation
behind a seam via a `RemoveAll<T>()` + `Add…` registration.

### Foundation
| Package | Summary | README |
|---|---|---|
| `Slice.Core` | Ambient abstractions (`IClock`, `ICurrentUser`, `ICurrentTenant`, `IGuidGenerator`, `IDataFilter`, `IDistributedLock`) + null defaults, DI markers, the `Result<T>` model | [↗](../src/Slice.Core/README.md) |
| `Slice.Domain` | Entities, aggregate roots, audited bases, value objects, repositories, specifications, `Ensure`, event interfaces | [↗](../src/Slice.Domain/README.md) |
| `Slice.Modularity` | `SliceModule`, `[DependsOn]`, the topological module loader, convention registrar | [↗](../src/Slice.Modularity/README.md) |

### Application & mediator
| Package | Summary | README |
|---|---|---|
| `Slice.Mediator` | Mediator abstraction: `ISender`, `IRequest<T>`, behaviors, pipeline ordering | [↗](../src/Slice.Mediator/README.md) |
| `Slice.Mediator.Default` | The built-in mediator engine (`AddSliceMediator`) | [↗](../src/Slice.Mediator.Default/README.md) |
| `Slice.Mediator.MediatR` | Adapter that swaps the engine for MediatR | [↗](../src/Slice.Mediator.MediatR/README.md) |
| `Slice.Application` | CQRS (`ICommand`/`IQuery`), the validation/logging/UoW behaviors, `ResultFactory` | [↗](../src/Slice.Application/README.md) |

### Web
| Package | Summary | README |
|---|---|---|
| `Slice.AspNetCore` | `SliceController`, `Result<T>` → HTTP mapping, ProblemDetails, exception middleware | [↗](../src/Slice.AspNetCore/README.md) |
| `Slice.ApiVersioning` | API versioning over Asp.Versioning | [↗](../src/Slice.ApiVersioning/README.md) |
| `Slice.AspNetCore.SignalR` | Tenant/user-aware SignalR hubs | [↗](../src/Slice.AspNetCore.SignalR/README.md) |
| `Slice.AspNetCore.Hypermedia` | HAL `_links`/`_embedded`, content-negotiated + permission-aware | [↗](../src/Slice.AspNetCore.Hypermedia/README.md) |
| `Slice.AspNetCore.ConditionalRequests` | `ETag`/`If-None-Match` → 304, `If-Match` → 412 | [↗](../src/Slice.AspNetCore.ConditionalRequests/README.md) |
| `Slice.AspNetCore.MinimalApi` | Minimal-API endpoints: `ISliceEndpoint`, `Result`→HTTP mapping, OpenAPI | [↗](../src/Slice.AspNetCore.MinimalApi/README.md) |

### Persistence
| Package | Summary | README |
|---|---|---|
| `Slice.EntityFrameworkCore` | `SliceDbContext`, repositories, interceptors, outbox/inbox, `AddSliceDbContext`, **database-per-tenant** | [↗](../src/Slice.EntityFrameworkCore/README.md) |
| `Slice.Dapper` | Dapper executor sharing the EF connection + transaction | [↗](../src/Slice.Dapper/README.md) |
| `Slice.LinqToDB` | LinqToDB `DataConnection` over the EF connection + transaction | [↗](../src/Slice.LinqToDB/README.md) |

### Multi-tenancy, security & management
| Package | Summary | README |
|---|---|---|
| `Slice.MultiTenancy` | Tenant resolution, ambient `ICurrentTenant`, the multi-tenancy middleware | [↗](../src/Slice.MultiTenancy/README.md) |
| `Slice.Authorization` | Permission definitions, checker, store, `[SlicePermission]`, the authorization behavior | [↗](../src/Slice.Authorization/README.md) |
| `Slice.Authentication` | OpenIddict server + ASP.NET Identity, `/connect/token`, current-user, seeding | [↗](../src/Slice.Authentication/README.md) |
| `Slice.Management` | DB-backed permission grants, tenants, setting/feature values + admin controllers | [↗](../src/Slice.Management/README.md) |

### Messaging
| Package | Summary | README |
|---|---|---|
| `Slice.EventBus` | Local + distributed event buses, the transport seam, event-name registry, inbox | [↗](../src/Slice.EventBus/README.md) |
| `Slice.EventBus.RabbitMQ` | RabbitMQ transport | [↗](../src/Slice.EventBus.RabbitMQ/README.md) |
| `Slice.EventBus.AzureServiceBus` | Azure Service Bus transport | [↗](../src/Slice.EventBus.AzureServiceBus/README.md) |
| `Slice.Kafka` | Kafka client (producer pool / consumer factory) | [↗](../src/Slice.Kafka/README.md) |
| `Slice.EventBus.Kafka` | Kafka distributed-event transport | [↗](../src/Slice.EventBus.Kafka/README.md) |

### Cross-cutting services
| Package | Summary | README |
|---|---|---|
| `Slice.Settings` | Setting definitions + layered value providers | [↗](../src/Slice.Settings/README.md) |
| `Slice.Features` | Feature flags + `[RequiresFeature]` | [↗](../src/Slice.Features/README.md) |
| `Slice.Localization` | Contributor-based localization | [↗](../src/Slice.Localization/README.md) |
| `Slice.Caching` | `ISliceCache` over `IDistributedCache` (tenant-isolated keys) | [↗](../src/Slice.Caching/README.md) |
| `Slice.Caching.Redis` | Redis cache backend | [↗](../src/Slice.Caching.Redis/README.md) |
| `Slice.BlobStoring` | `IBlobProvider`/typed containers; FileSystem default | [↗](../src/Slice.BlobStoring/README.md) |
| `Slice.BlobStoring.Aws` | AWS S3 backend | [↗](../src/Slice.BlobStoring.Aws/README.md) |
| `Slice.BlobStoring.Azure` | Azure Blob Storage backend | [↗](../src/Slice.BlobStoring.Azure/README.md) |
| `Slice.BlobStoring.Minio` | MinIO backend | [↗](../src/Slice.BlobStoring.Minio/README.md) |
| `Slice.Emailing` | `IEmailSender`; Null + SMTP | [↗](../src/Slice.Emailing/README.md) |
| `Slice.Emailing.MailKit` | MailKit sender (attachments + multi-recipient) | [↗](../src/Slice.Emailing.MailKit/README.md) |
| `Slice.BackgroundJobs` | Fire-and-forget + recurring jobs (in-memory default) | [↗](../src/Slice.BackgroundJobs/README.md) |
| `Slice.BackgroundJobs.Hangfire` | Hangfire-backed jobs | [↗](../src/Slice.BackgroundJobs.Hangfire/README.md) |
| `Slice.BackgroundWorkers` | Periodic background workers | [↗](../src/Slice.BackgroundWorkers/README.md) |
| `Slice.DistributedLocking` | `IDistributedLock` local default | [↗](../src/Slice.DistributedLocking/README.md) |
| `Slice.DistributedLocking.Redis` | Redis distributed lock | [↗](../src/Slice.DistributedLocking.Redis/README.md) |
| `Slice.ObjectMapping` | `IObjectMapper` + typed mappers | [↗](../src/Slice.ObjectMapping/README.md) |
| `Slice.VirtualFileSystem` | Composite embedded + physical file provider | [↗](../src/Slice.VirtualFileSystem/README.md) |
| `Slice.Serilog` | Serilog host integration + request logging | [↗](../src/Slice.Serilog/README.md) |

### PostgreSQL adapters (run everything on Postgres — or just one thing) — see [the guide](postgresql-stack.md)

`Slice.Postgres` is the shared foundation (one `NpgsqlDataSource`); **each adapter below is independently
usable** via its own `AddSlicePostgresXxx()` on top of it — you don't need `Slice.PostgresStack`, which is
just the one-call aggregator of those same registrations.

| Package | Summary | README |
|---|---|---|
| `Slice.Postgres` | Shared `NpgsqlDataSource` pool + schema initializer | [↗](../src/Slice.Postgres/README.md) |
| `Slice.PostgresStack` | One-call `AddSlicePostgresStack` wiring the whole stack | [↗](../src/Slice.PostgresStack/README.md) |
| `Slice.EntityFrameworkCore.PostgreSQL` | `UseSlicePostgres` — EF data + outbox/inbox on Postgres | [↗](../src/Slice.EntityFrameworkCore.PostgreSQL/README.md) |
| `Slice.Caching.Postgres` | `IDistributedCache` over `slice_cache` | [↗](../src/Slice.Caching.Postgres/README.md) |
| `Slice.DistributedLocking.Postgres` | Advisory-lock `IDistributedLock` | [↗](../src/Slice.DistributedLocking.Postgres/README.md) |
| `Slice.EventBus.Postgres` | Event-bus transport (LISTEN/NOTIFY + SKIP LOCKED) | [↗](../src/Slice.EventBus.Postgres/README.md) |
| `Slice.BackgroundJobs.Postgres` | Durable job queue | [↗](../src/Slice.BackgroundJobs.Postgres/README.md) |
| `Slice.BlobStoring.Postgres` | Blob backend (`bytea`) | [↗](../src/Slice.BlobStoring.Postgres/README.md) |
| `Slice.Vector` | `IVectorStore` + `IEmbeddingGenerator` seam | [↗](../src/Slice.Vector/README.md) |
| `Slice.Vector.Postgres` | pgvector store | [↗](../src/Slice.Vector.Postgres/README.md) |
| `Slice.Embeddings.OpenAI` | OpenAI-compatible embedder (OpenAI/Azure/Ollama) | [↗](../src/Slice.Embeddings.OpenAI/README.md) |

> Conventions for skipped ABP libraries (Auditing, FluentValidation, Timing, RabbitMQ client, Mapperly)
> are already covered by `Slice.EntityFrameworkCore` (auditing interceptor), `Slice.Application`
> (FluentValidation behavior), `Slice.Core` (`IClock`), `Slice.EventBus.RabbitMQ`, and
> `Slice.ObjectMapping` respectively — see [Architecture](architecture.md#abp-parity).
