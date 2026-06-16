# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

**Slice** is a .NET 10 application framework (ABP-style) built around **Vertical Slice Architecture** +
DDD: modules, convention-based DI, a pluggable mediator pipeline, DDD building blocks, and
Result-based error handling. The `README.md` carries the full feature inventory (phases P1–P22) and
`docs/` holds the conceptual documentation (start at `docs/README.md`); per-package API surface lives
in each `src/*/README.md`.

## Commands

```bash
dotnet build Slice.slnx                          # build everything (solution is .slnx, not .sln)
dotnet run --project samples/Slice.Sample.Crm    # run the reference app (SQLite, auto-created)

dotnet test Slice.slnx                           # full suite — Docker required (Testcontainers)
# fast subset, no Docker:
dotnet test Slice.slnx --filter "FullyQualifiedName!~RabbitMQ&FullyQualifiedName!~Kafka&FullyQualifiedName!~Minio&FullyQualifiedName!~MailKit"

dotnet test tests/Slice.Domain.Tests             # one test project
dotnet test Slice.slnx --filter "FullyQualifiedName~CreateLead"   # one test / class

# packaging + templates
dotnet pack Slice.slnx -c Release -o artifacts
dotnet pack templates/Slice.Templates.csproj -o artifacts
dotnet new install ./artifacts/Slice.Templates.0.1.0.nupkg   # exposes slice-api / slice-module / slice-feature
```

Reference app auth: `POST /connect/token` (password grant, `admin@slice` / `Admin123!`), then call
`/api/crm/leads` with `Authorization: Bearer <token>`. Any host that calls `AddSliceOpenApi()` +
`MapSliceOpenApi()` (helpers in `Slice.AspNetCore`, work for controllers and minimal APIs) serves an
OpenAPI document at `/openapi/v1.json` and an interactive Scalar UI at `/scalar/v1`.

## Build conventions

- `Directory.Build.props` sets all projects to **`net10.0`**, `Nullable=enable`, `ImplicitUsings=enable`.
- **Central Package Management** — all versions live in `Directory.Packages.props`. Add a dependency
  with `<PackageReference Include="X" />` (no `Version`) and register the version centrally.
- Projects are registered in `Slice.slnx`; add new `src/`, `tests/`, `samples/` projects there.

## Architecture (the parts that span files)

**Layering.** `Slice.Core` (Result/Error, DI markers, ambient contracts) → `Slice.Domain` (Entity /
AggregateRoot / ValueObject, repositories, specifications) → `Slice.Modularity` → `Slice.Mediator`
(the abstraction seam) → `Slice.Application` (CQRS + cross-cutting behaviors) → `Slice.AspNetCore`.
Everything else (`Slice.EntityFrameworkCore`, `Slice.MultiTenancy`, persistence/messaging/storage
adapters) is an **opt-in module**. Concrete transports (`.Redis`, `.Kafka`, `.RabbitMQ`, `.Azure`,
`.Aws`, `.Postgres`, `.Hangfire`, …) are separate packages behind a seam in the base package.

**Modules.** Each package/bounded-context is a `SliceModule` subclass that declares its dependencies
with `[DependsOn(typeof(...))]`; modules are configured in **topological order** via
`AddSliceModules<TRoot>(config)` + `await InitializeSliceModulesAsync()`. Lifecycle hooks:
`PreConfigureServices` / `ConfigureServices` / `PostConfigureServices`, then
`OnApplicationInitializationAsync` / `OnApplicationShutdownAsync`. Cross-module handshakes use
`ServiceConfigurationContext.Items`.

**Convention DI.** A service is registered by *implementing a marker interface* (defined in
`Slice.Core/DependencyInjection/DependencyMarkers.cs`): `ITransientDependency`, `IScopedDependency`,
`ISingletonDependency`. The module's assembly scan (`ConventionalRegistrar`) discovers and registers
them — plus handlers, validators, repositories. **Adding a slice needs no central wiring.**

**Mediator pipeline.** `Controller → ISender → behaviors → Handler`. Behaviors implement
`IPipelineBehavior<,>`; ordering is **deterministic** via `IHasPipelineOrder` + the constants in
`src/Slice.Mediator/PipelineOrder.cs` (Logging 100 → MultiTenancy 200 → Authorization 300 →
FeatureCheck 350 → Validation 400 → UnitOfWork 500 → handler). Registration order is irrelevant. Two
engines satisfy the seam interchangeably: `Slice.Mediator.Default` (custom) and `Slice.Mediator.MediatR`
(adapter via `AddSliceMediatorMediatR`); conformance tests assert identical behavior.

**Error handling.** Business outcomes flow as `Result` / `Result<T>` / `Error` (mapped to HTTP status
codes by `Slice.AspNetCore`). **Only programming/infra faults throw** (caught by exception middleware).
Don't throw for expected business failures — return a failed `Result`.

**Anatomy of a slice.** One use case = one folder under `Features/<Name>/` containing the command/query
(`ICommand<Result<T>>` / `IQuery<...>`, optionally `[SlicePermission(...)]`), its FluentValidation
validator, the handler (`ICommandHandler<,>` returning `Result<T>`), and a thin `SliceController`
(or `ISliceEndpoint` minimal API) that just calls `SendAsync(command)`. See
`samples/Slice.Sample.Crm/Features/CreateLead/` as the canonical example.

**Unit of work.** Command handlers call repository writes with **`autoSave: false`**; the
`UnitOfWorkBehavior` commits after the command succeeds, which fires the auditing, soft-delete, and
domain-event interceptors. Don't `SaveChanges` inside a handler.

**Samples** (under `samples/`) are the living integration tests of the wiring: `Slice.Sample.Crm`
(full feature set, SQLite), `Slice.Sample.PostgresStack` (one Postgres for everything),
`modular-monolith/` (4 modules, EF/LinqToDB/Dapper sharing in-process events),
`Slice.Sample.MultiTenant` (database-per-tenant), `Slice.Sample.MinimalApi`.

## Knowledge graph (graphify)

A graphify knowledge graph may live at `graphify-out/`. Per the global project rule: read
`graphify-out/GRAPH_REPORT.md` before architecture questions, prefer `graphify-out/wiki/index.md` over
raw files when it exists, and after modifying code files run:

```bash
python3 -c "from graphify.watch import _rebuild_code; from pathlib import Path; _rebuild_code(Path('.'))"
```
