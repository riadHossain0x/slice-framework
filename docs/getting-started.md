# Getting started

## Prerequisites

- **.NET 10 SDK**
- Optional: **Docker** (only for the broker/storage integration tests — RabbitMQ, Kafka, MinIO, SMTP)

## Build & run the sample

The repository ships a runnable bounded context, `Slice.Sample.Crm`, that exercises most of the
framework.

```bash
git clone <repo> slice-framework
cd slice-framework

dotnet build Slice.slnx
dotnet run --project samples/Slice.Sample.Crm
```

The sample uses SQLite (auto-created on first run) and seeds a demo admin. Get a token and call a
protected endpoint:

```bash
# 1. get an access token (password grant; demo admin seeded by Slice.Authentication)
curl -X POST http://localhost:5273/connect/token \
  --data-urlencode grant_type=password \
  --data-urlencode username=admin@slice \
  --data-urlencode password=Admin123! \
  --data-urlencode 'scope=api offline_access'

# 2. call a protected endpoint with the returned access_token
curl -X POST http://localhost:5273/api/crm/leads \
  -H "Authorization: Bearer <access_token>" \
  -H "Content-Type: application/json" \
  -d '{"firstName":"Ada","lastName":"Lovelace","email":"ada@x.com","source":0}'
```

> The demo OpenIddict server accepts anonymous clients (no `client_id` required) and registers the
> `api` and `offline_access` scopes. Dev signing/encryption use ephemeral keys. See
> [Security](security.md) for production hardening.

## Run the tests

```bash
# fast suite (no Docker): domain, mediator conformance, architecture, data (Dapper/LinqToDB/DB-per-tenant),
# Serilog, SignalR
dotnet test Slice.slnx --filter "FullyQualifiedName!~RabbitMQ&FullyQualifiedName!~Kafka&FullyQualifiedName!~Minio&FullyQualifiedName!~MailKit"

# full suite (needs Docker for Testcontainers: RabbitMQ, Kafka, MinIO, Mailpit SMTP)
dotnet test Slice.slnx
```

See [Testing](testing.md).

---

## Scaffold a new application from templates

Slice ships seven `dotnet new` templates in the `Slice.Templates` package.

```bash
# one-time: build the packages + the template package, install the templates
dotnet pack Slice.slnx -c Release -o artifacts
dotnet pack templates/Slice.Templates.csproj -o artifacts
dotnet new install ./artifacts/Slice.Templates.0.1.0.nupkg
```

| Template | Short name | Produces |
|---|---|---|
| Slice API host | `slice-api` | A full, runnable **controller** host: `Program.cs` + root `AppModule` (auth + management + EF wired), a `Note` aggregate, `CreateNote`/`ListNotes` slices, `appsettings`, `nuget.config`. `--database sqlite\|postgres` |
| Slice minimal-API host | `slice-api-minimal` | A controller-free host: `ISliceEndpoint` slices (`CreateNote`/`GetNote`/`ListNotes`), HAL + ETag + OpenAPI/Scalar, versioned group |
| Slice bounded-context module | `slice-module` | A classlib bounded context: module + `DbContext` + `Item` aggregate + repository + permission provider + a feature |
| Slice modular monolith | `slice-monolith` | A multi-project **solution**: `Host` + `Orders` + `Billing` modules + shared `Contracts`, choreographed by in-process distributed events (Orders → `OrderPlacedEto` → Billing → `InvoiceCreatedEto`) |
| Slice worker host | `slice-worker` | A headless console host (no web): module composition + a periodic `IBackgroundWorker`. For background jobs / scheduled work |
| Slice database-per-tenant API | `slice-tenant-api` | A db-per-tenant API (tenant registry + runtime `POST /api/tenants` onboarding + EF migrations). `--migrations host` (default) migrates in-process at startup (single project); `--migrations job` adds a separate `.Migrator` console job and turns off in-host migration |
| Slice feature slice | `slice-feature` | A single vertical slice (command + validator + handler + controller) to drop into a module |

```bash
# create a runnable API
dotnet new slice-api -n Acme.Shop -o Acme.Shop
cd Acme.Shop

# until Slice.* is on nuget.org, point restore at the local artifacts feed
dotnet nuget add source ../artifacts -n slice-local --configfile nuget.config
dotnet run     # admin@slice / Admin123! ; POST /connect/token then call /api/notes

# add a bounded context, then reference it from your host's root module [DependsOn]
dotnet new slice-module -n Billing -o Billing

# add a vertical slice into a module (set --module to that module's namespace)
dotnet new slice-feature -n ArchiveNote --module Acme.Shop
```

Other starters (each ships its own `nuget.config` — add the local feed the same way):

```bash
dotnet new slice-api-minimal -n Acme.Api -o Acme.Api      # controller-free minimal-API host
dotnet new slice-monolith    -n Acme     -o Acme          # solution: Host + Orders + Billing + Contracts
dotnet new slice-worker      -n Acme.Jobs -o Acme.Jobs     # headless background-worker host
dotnet new slice-tenant-api  -n Acme.Tenants -o Acme.Tenants                   # db-per-tenant API (in-host migration)
dotnet new slice-tenant-api  -n Acme.Tenants -o Acme.Tenants --migrations job  # … + a separate migration job
```

---

## Your first vertical slice (by hand)

A slice is a single file with four parts. Drop it under `Features/<Name>/`:

```csharp
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Slice.Application;          // ICommand / ICommandHandler
using Slice.AspNetCore.Mvc;       // SliceController
using Slice.Authorization;        // [SlicePermission]
using Slice.Core.Ambient;         // IGuidGenerator, ICurrentTenant
using Slice.Core.Results;         // Result<T>

namespace Acme.Shop.Features.CreateNote;

// 1) the command — what the caller asks for. Gated by a permission.
[SlicePermission(AppPermissions.Notes.Create)]
public sealed record CreateNoteCommand(string Title, string Body) : ICommand<Result<Guid>>;

// 2) the validator — FluentValidation, run automatically by ValidationBehavior.
public sealed class CreateNoteValidator : AbstractValidator<CreateNoteCommand>
{
    public CreateNoteValidator() => RuleFor(x => x.Title).NotEmpty().MaximumLength(256);
}

// 3) the handler — pure use-case logic. autoSave:false → the UoW behavior commits on success.
public sealed class CreateNoteHandler(INoteRepository repository, IGuidGenerator guids, ICurrentTenant tenant)
    : ICommandHandler<CreateNoteCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(CreateNoteCommand command, CancellationToken ct)
    {
        var note = new Note(guids.Create(), tenant.Id, command.Title, command.Body);
        await repository.InsertAsync(note, autoSave: false, ct);
        return Result<Guid>.Success(note.Id);
    }
}

// 4) the controller — thin; just forwards to the mediator. Result<T> → HTTP is automatic.
[Authorize]
[Route("api/notes")]
public sealed class CreateNoteController : SliceController
{
    [HttpPost]
    public Task<IActionResult> Create([FromBody] CreateNoteCommand command, CancellationToken ct)
        => SendAsync(command, ct);
}
```

You did **not** register the handler, the validator, or the controller anywhere. They are discovered
by the module's assembly scan (`AddRequestHandlers`, `AddValidatorsFromAssembly`, MVC controller
discovery). The permission is enforced by `AuthorizationBehavior`; tenancy, validation and the unit
of work are applied by the pipeline. Continue with [CQRS & the mediator pipeline](cqrs-and-mediator.md).
