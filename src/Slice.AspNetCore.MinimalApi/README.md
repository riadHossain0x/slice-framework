# Slice.AspNetCore.MinimalApi

> Minimal-API support for Slice: dispatch through `ISender`, map `Result<T>` → HTTP, and self-register feature endpoints — with the same pipeline (validation, permissions, multitenancy, unit-of-work) controllers get.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

Slice's mediator pipeline is HTTP-agnostic, so a minimal-API endpoint that calls `ISender.SendAsync(request)`
gets logging, multitenancy, **`[SlicePermission]` authorization**, validation, and unit-of-work exactly like
a controller. This package supplies the thin web edge:

- **`SliceResults.ToHttpResult`** + **`SliceResultEndpointFilter`** map the framework's
  `Slice.Core.Results.IResult` to an HTTP result (reusing `ProblemDetailsMapper`), so an endpoint delegate is
  just `(...) => sender.SendAsync(request, ct)`.
- **`ISliceEndpoint` + `MapSliceEndpoints(assembly, configure)`** discover and map each feature's endpoints
  onto a group pre-wired with the result filter — keeping "a feature is a folder" (no central wiring per feature).
- **`AddSliceOpenApi` / `MapSliceOpenApi`** (from [`Slice.AspNetCore`](../Slice.AspNetCore/README.md)) opt into the built-in OpenAPI document **and** a Scalar UI at `/scalar/v1`.

It composes with [`Slice.AspNetCore.Hypermedia`](../Slice.AspNetCore.Hypermedia/README.md) (`.AddHal()`/`.WithHal()`)
and [`Slice.AspNetCore.ConditionalRequests`](../Slice.AspNetCore.ConditionalRequests/README.md)
(`.AddResourceVersion()`/`.WithResourceVersion()`) for HAL + ETag parity with controllers.

## Dependencies

- **Slice:** `Slice.AspNetCore` (reuses `ProblemDetailsMapper`), `Slice.Mediator`, `Slice.Modularity`
- **Third-party:** `FrameworkReference Microsoft.AspNetCore.App` (OpenAPI + Scalar come transitively via `Slice.AspNetCore`)

## Usage

A feature is a folder: command/query + handler + a tiny `ISliceEndpoint` next to them.

```csharp
[SlicePermission(NotesPermissions.Create)]
public sealed record CreateNoteCommand(string Title, string Body) : ICommand<Result<Guid>>;

public sealed class CreateNoteEndpoint : ISliceEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
        => endpoints.MapPost("/v1/notes", (CreateNoteCommand command, ISender sender, CancellationToken ct)
                => sender.SendAsync(command, ct))   // returns Result<Guid>; the result filter maps it
            .WithName("CreateNote")
            .Produces<Guid>()
            .ProducesValidationProblem();
}
```

Wire it once in `Program.cs`:

```csharp
builder.Services.AddSliceModules<AppModule>(builder.Configuration);
builder.Services.AddSliceApiVersioning();
builder.Services.AddSliceOpenApi();

var app = builder.Build();
await app.Services.InitializeSliceModulesAsync();

app.UseSliceExceptionHandling();
app.UseSliceConditionalRequests();

var versionSet = app.NewSliceApiVersionSet(1.0);
app.MapSliceEndpoints(
    typeof(AppModule).Assembly,
    group => group.WithApiVersionSet(versionSet)
                  .RequireAuthorization()      // authentication (the [Authorize] analogue)
                  .AddHal()                    // HAL when application/hal+json is negotiated
                  .AddResourceVersion());      // cheap ETag from IHasResourceVersion
app.MapSliceOpenApi();

app.Run();
```

Minimal APIs and controllers can coexist in one app (call both `MapControllers()` and `MapSliceEndpoints(...)`).

## Key types

| Type | Kind | Description |
|---|---|---|
| `SliceResults.ToHttpResult` | static | Maps `Slice.Core.Results.IResult` → `Microsoft.AspNetCore.Http.IResult` (200/204/Problem). |
| `SliceResultEndpointFilter` | endpoint filter | Applies `ToHttpResult` to a returned framework result; attached automatically by `MapSliceEndpoints`. |
| `ISliceEndpoint` | interface | `void Map(IEndpointRouteBuilder)` — one per feature; discovered by assembly scan. |
| `SliceEndpointExtensions.MapSliceEndpoints(assembly, configure?)` | extension | Discovers + maps endpoints onto a group pre-wired with the result filter; `configure` adds group conventions (auth, version set, HAL, ETag). |
| `SliceOpenApiExtensions.AddSliceOpenApi` / `MapSliceOpenApi` | extension | Built-in OpenAPI document + Scalar UI at `/scalar/v1` (lives in `Slice.AspNetCore`). |

## Notes

- **`IResult` collision:** the framework's `Slice.Core.Results.IResult` and ASP.NET's
  `Microsoft.AspNetCore.Http.IResult` share a name; this package aliases them internally. In your endpoints you
  only deal with the framework result (returned by `SendAsync`).
- The result filter is attached by `MapSliceEndpoints` so it can't be forgotten; any filters you add in
  `configure` (HAL/ETag) run *inside* it and see the raw `Result<T>`.
- Authentication is per-endpoint/group via `.RequireAuthorization()`; permission checks stay on the request
  type via `[SlicePermission]` (enforced in the pipeline, so background callers are covered too).
