# Minimal APIs

Slice is controller-first ([`SliceController`](web-and-results.md#slicecontroller)), but it supports
**minimal APIs** as a first-class alternative — and the two coexist in one app. Because the mediator
pipeline is HTTP-agnostic, a minimal-API endpoint that dispatches through `ISender` gets the *same*
cross-cutting behavior a controller does: logging, multitenancy, `[SlicePermission]` authorization,
validation, and unit-of-work. The `Slice.AspNetCore.MinimalApi` package supplies the thin web edge.

## A feature is (still) a folder

Put the command/query, handler, validator, and a small `ISliceEndpoint` together. The endpoint dispatches
through `ISender` and returns the framework's `Result<T>` — a filter maps it to HTTP.

```csharp
[SlicePermission(NotesPermissions.Create)]
public sealed record CreateNoteCommand(string Title, string Body) : ICommand<Result<Guid>>;

public sealed class CreateNoteValidator : AbstractValidator<CreateNoteCommand> { /* … */ }

public sealed class CreateNoteHandler(INoteRepository repo, IGuidGenerator guids)
    : ICommandHandler<CreateNoteCommand, Result<Guid>> { /* … */ }

public sealed class CreateNoteEndpoint : ISliceEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
        => endpoints.MapPost("/v1/notes", (CreateNoteCommand command, ISender sender, CancellationToken ct)
                => sender.SendAsync(command, ct))
            .WithName("CreateNote")
            .Produces<Guid>()
            .ProducesValidationProblem();
}
```

No central edit is needed per feature: `MapSliceEndpoints(assembly)` discovers every `ISliceEndpoint`.

## Wiring (Program.cs)

```csharp
builder.Services.AddSliceModules<AppModule>(builder.Configuration);
builder.Services.AddSliceApiVersioning();
builder.Services.AddSliceOpenApi();

var app = builder.Build();
await app.Services.InitializeSliceModulesAsync();

app.UseSliceExceptionHandling();
app.UseSliceConditionalRequests();            // ETag/304 + If-Match/412

var versionSet = app.NewSliceApiVersionSet(1.0);
app.MapSliceEndpoints(
    typeof(AppModule).Assembly,
    group => group.WithApiVersionSet(versionSet)
                  .RequireAuthorization()      // authentication (the [Authorize] analogue)
                  .AddHal()                    // HAL when application/hal+json is negotiated
                  .AddResourceVersion());      // cheap strong ETag from IHasResourceVersion
app.MapSliceOpenApi();                          // OpenAPI at /openapi/v1.json

app.Run();
```

`MapSliceEndpoints` creates one route group, attaches the **result-mapping filter** (so it can't be
forgotten), runs your `configure` to add group conventions, then maps every discovered endpoint onto it.

## Result mapping

`SliceResultEndpointFilter` turns the returned `Slice.Core.Results.IResult` into an HTTP result, reusing the
same `ProblemDetailsMapper` controllers use — so status codes and ProblemDetails bodies are identical:

| Outcome | HTTP |
|---|---|
| `Result<T>.Success(value)` | `200 OK` + value |
| success with `null` value | `204 No Content` |
| `Error.Validation` (with details) | `400` ValidationProblemDetails |
| `Error.NotFound` / `Conflict` / `Forbidden` / `Unauthorized` | `404` / `409` / `403` / `401` |

## Authorization & validation

- **Permissions**: keep `[SlicePermission(...)]` on the command/query — `AuthorizationBehavior` enforces it in
  the pipeline for every caller (HTTP or background). Add `.RequireAuthorization()` on the group/endpoint for
  authentication (the minimal-API `[Authorize]`).
- **Validation**: FluentValidation validators run in the pipeline; a failure short-circuits to a
  `400` ValidationProblemDetails before the handler.

## HAL & conditional requests (parity with controllers)

Add the endpoint filters and minimal APIs behave exactly like controllers:

- **`.AddHal()` / `.WithHal()`** ([`Slice.AspNetCore.Hypermedia`](hypermedia-and-caching.md)) — when the client
  sends `Accept: application/hal+json`, the response gains `_links`/`_embedded` from the registered
  `IResourceLinkContributor<T>` (permission-aware). For minimal-API named routes, resolve links with
  `links.AddRoute("self", "GetNote", new { id })` (endpoint names set via `.WithName(...)`).
- **`.AddResourceVersion()` / `.WithResourceVersion()`** ([`Slice.AspNetCore.ConditionalRequests`](hypermedia-and-caching.md))
  — a GET whose value is `IHasResourceVersion` gets a strong `ETag` (cheap validator, no body hash); the
  conditional-request middleware then answers `If-None-Match` with `304`. Writes that fail `If-Match` map to
  `412` (via the middleware) — pass `HttpContext.GetIfMatch()` into your command and apply it with
  `db.Entry(entity).UseIfMatch(ifMatch)`.

## Versioning & OpenAPI

`AddSliceApiVersioning()` configures both MVC and minimal APIs. Build a version set with
`app.NewSliceApiVersionSet(1.0)` and attach it to the group with `.WithApiVersionSet(...)`; responses report
`api-supported-versions`. `AddSliceOpenApi()` + `MapSliceOpenApi()` publish an OpenAPI document
(`/openapi/v1.json`) **and** an interactive [Scalar](https://scalar.com) UI at `/scalar/v1`; describe
endpoints with `WithName`/`WithTags`/`Produces<T>`/`ProducesProblem`. The helpers live in
`Slice.AspNetCore` (not the minimal-API package), so **controller-based hosts get the same document + UI** —
just call `AddSliceOpenApi()` and `MapSliceOpenApi()` from any Slice host. Pass `MapSliceOpenApi(mapUi: false)`
to publish only the JSON document (e.g. to drop the UI in production).

## Coexisting with controllers

Call both — controllers and minimal APIs share one pipeline and the same handlers:

```csharp
app.MapControllers();
app.MapSliceEndpoints(typeof(AppModule).Assembly, g => g.RequireAuthorization().AddHal().AddResourceVersion());
```

The **CRM sample** does exactly this (the same `GetLeadQuery` is reachable at `/api/crm/leads/{id}` via a
controller and `/api/min/crm/leads/{id}` via a minimal endpoint). The **`Slice.Sample.MinimalApi`** sample is a
controller-free showcase (versioned group + OpenAPI + HAL + ETag). See the package
[README](../src/Slice.AspNetCore.MinimalApi/README.md).
