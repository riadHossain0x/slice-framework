# Slice.AspNetCore

> ASP.NET Core web host integration: a thin controller base, Result-to-HTTP mapping, and a last-resort exception handler.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

This module wires the framework into the ASP.NET Core MVC pipeline. It provides `SliceController`, a thin controller base that dispatches requests through the mediator and translates `Result`/`Result<T>` outcomes into `IActionResult`s. `ProblemDetailsMapper` centralises how framework `Error`s become HTTP status codes and RFC 7807 `ProblemDetails`, and `SliceExceptionMiddleware` is the safety net that maps any escaped domain exception (or unexpected fault) to a problem response.

## Dependencies

- **Slice:** `Slice.Application` (depended on via `SliceApplicationModule`), `Slice.Mediator` (`ISender`, `IRequest<TResponse>`), `Slice.Core` (`Result`, `Error`, `ErrorType`, `IResult`), `Slice.Domain` (domain exceptions), `Slice.Modularity`.
- **Third-party:** `Microsoft.AspNetCore.App` framework reference (MVC, ProblemDetails), `Microsoft.AspNetCore.OpenApi` + `Scalar.AspNetCore` (the OpenAPI document + Scalar UI helpers).

## Module & registration

`SliceAspNetCoreModule` is a `SliceModule` with `[DependsOn(typeof(SliceApplicationModule))]`. It registers MVC controllers and the ProblemDetails service; slice-local controllers are discovered by the standard application-part scan of referenced assemblies.

```csharp
[DependsOn(typeof(SliceAspNetCoreModule))]
public sealed class MyAppModule : SliceModule { }

// In the host pipeline:
app.UseSliceExceptionHandling(); // map escaped exceptions to ProblemDetails
app.MapControllers();
```

`ConfigureServices` calls `services.AddControllers()` and `services.AddProblemDetails()`.

## Key types

| Type | Kind | Description |
|---|---|---|
| `SliceAspNetCoreModule` | `sealed class : SliceModule` | Web module; enables MVC + ProblemDetails. |
| `SliceController` | `abstract class : ControllerBase` | `[ApiController]` base. Resolves `ISender` lazily; `SendAsync<TResponse>(IRequest<TResponse>, CancellationToken)` and `ToActionResult(IResult)`. |
| `ProblemDetailsMapper` | `static class` | `StatusFor(ErrorType)` and `ToProblemDetails(Error)`. |
| `SliceExceptionMiddleware` | `sealed class` | `InvokeAsync(HttpContext)`; maps exceptions to ProblemDetails, logs only `ErrorType.Unexpected`. |
| `SliceExceptionMiddlewareExtensions` | `static class` | `UseSliceExceptionHandling(this IApplicationBuilder)`. |
| `SliceOpenApiExtensions` | `static class` | `AddSliceOpenApi(documentName)` + `MapSliceOpenApi(mapUi)` — publish the built-in OpenAPI document (`/openapi/v1.json`) and a Scalar UI (`/scalar/v1`). Works for controller **and** minimal-API hosts. |

## Usage

```csharp
[Route("api/leads")]
public sealed class LeadsController : SliceController
{
    [HttpPost]
    public Task<IActionResult> Create(CreateLead command, CancellationToken ct)
        => SendAsync(command, ct);
}
```

`SendAsync` sends the request through `Sender`. If the response implements `IResult` it is mapped via `ToActionResult`; otherwise the raw response is returned as `200 OK`.

### OpenAPI + Scalar UI

Opt into an OpenAPI document and an interactive [Scalar](https://scalar.com) UI with one line each —
the same helpers serve controller hosts and minimal-API hosts:

```csharp
builder.Services.AddSliceOpenApi();   // OpenAPI document services
// ...
app.MapControllers();
app.MapSliceOpenApi();                 // /openapi/v1.json + Scalar UI at /scalar/v1
```

Pass `MapSliceOpenApi(mapUi: false)` to publish only the JSON document (e.g. to drop the UI in
production). Built-in OpenAPI documents controller actions via ApiExplorer (`SliceController` is
`[ApiController]`), so no extra wiring is needed.

## Notes

- **Result-to-HTTP mapping** (`SliceController.ToActionResult`): on success, a `null` value yields `204 No Content`, otherwise `200 OK` with the value. On failure, the status comes from `ProblemDetailsMapper.StatusFor(error.Type)`.
- **`ProblemDetailsMapper.StatusFor`**: `Validation → 400`, `NotFound → 404`, `Conflict → 409`, `Forbidden → 403`, `Unauthorized → 401`, anything else → `500`.
- **`ToProblemDetails`**: validation errors with `Details` produce a `ValidationProblemDetails`; all responses set `Title = error.Message` and add an `Extensions["code"] = error.Code`.
- **Exception middleware** is a safety net, not the primary failure path — expected business outcomes should travel as `Result`, not exceptions. It maps `AppValidationException → Validation`, `EntityNotFoundException → NotFound`, `BusinessRuleException → Conflict`, `SlicePipelineException` carries its own `Error`, and everything else becomes `Error.Unexpected("Server:Unexpected", ...)` (the only case logged at error level).
