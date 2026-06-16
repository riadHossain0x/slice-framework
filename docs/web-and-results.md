# Web API & results

`Slice.AspNetCore` provides the thin web layer: a controller base that forwards to the mediator, the
`Result<T>` → HTTP mapping, RFC-7807 ProblemDetails, and centralized exception handling.

---

## The Result model

Expected failures travel as data, not exceptions. `Slice.Core` defines:

```csharp
public enum ErrorType { Failure, Validation, NotFound, Conflict, Forbidden, Unauthorized }

public sealed record Error(string Code, string Message, ErrorType Type = ErrorType.Failure,
                           IReadOnlyDictionary<string, string[]>? Details = null)
{
    public static Error Validation(string code, string message, IReadOnlyDictionary<string,string[]> details);
    public static Error NotFound(string code, string message);
    public static Error Conflict(string code, string message);
    public static Error Forbidden(string code, string message);
    public static Error Unauthorized(string code, string message);
    public static Error Failure(string code, string message);
}

public interface IResult { bool IsSuccess { get; } Error? Error { get; } }

public sealed class Result : IResult            // no value
{
    public static Result Success();
    public static Result Failure(Error error);
}
public sealed class Result<T> : IResult         // carries a value on success
{
    public static Result<T> Success(T value);
    public static Result<T> Failure(Error error);
}
```

Handlers return `Result`/`Result<T>`. Build failures with the typed `Error` factories so the right HTTP
status is produced downstream:

```csharp
return lead is null
    ? Result<LeadDto>.Failure(Error.NotFound("Lead.NotFound", "Lead not found."))
    : Result<LeadDto>.Success(LeadDto.From(lead));
```

---

## SliceController

```csharp
[Authorize]
[Route("api/crm/leads")]
public sealed class GetLeadController : SliceController
{
    [HttpGet("{id:guid}")]
    public Task<IActionResult> Get(Guid id, CancellationToken ct) => SendAsync(new GetLeadQuery(id), ct);
}
```

`SliceController.SendAsync<TResponse>(IRequest<TResponse>, ct)`:

1. dispatches the request through `ISender` (the full pipeline: logging, tenancy, authorization,
   feature check, validation, unit of work, handler);
2. maps the returned `Result`/`Result<T>` to an `IActionResult`.

You can also map a `Result` you already have with the controller's conversion helper.

---

## Result → HTTP mapping

`ProblemDetailsMapper` decides the status code and body:

| Outcome | HTTP status |
|---|---|
| `Result.Success()` (no value) | **204 No Content** |
| `Result<T>.Success(value)` | **200 OK** + the value |
| `Error.Type == Validation` | **400** (`ValidationProblemDetails` when `Details` present) |
| `Error.Type == Unauthorized` | **401** |
| `Error.Type == Forbidden` | **403** |
| `Error.Type == NotFound` | **404** |
| `Error.Type == Conflict` | **409** |
| `Error.Type == Failure` (or unknown) | **500** |

Failure responses are RFC-7807 ProblemDetails with the `Error.Code` carried in an extension member, so
clients get a machine-readable code plus a human message. Validation errors include the per-field
`Details` dictionary.

---

## Exception handling

`UseSliceExceptionHandling()` installs `SliceExceptionMiddleware`, which catches unhandled exceptions,
maps known domain exceptions to the appropriate `Error`/status, and renders ProblemDetails (logging the
rest as 500). Put it first in the pipeline:

```csharp
app.UseSliceExceptionHandling();   // outermost
app.UseSliceLocalization();
app.UseSliceAuthentication();
app.UseSliceMultiTenancy();
app.MapControllers();
```

Mapping highlights (domain exception → status): validation → 400, not-found → 404, business-rule →
409/400, unauthorized/forbidden → 401/403, anything else → 500. This means a handler *may* throw a
domain exception for a genuinely exceptional path and still get a clean ProblemDetails response — but
prefer returning a `Result` for *expected* failures so they flow through the pipeline without unwinding
the stack.

---

## Middleware order

The recommended pipeline order (from the sample and the `slice-api` template):

```csharp
app.UseSliceExceptionHandling();      // 1. catch + ProblemDetails
app.UseSliceConditionalRequests();    //    ETag/304 + If-Match/412 (inside the exception handler), if used
app.UseSliceLocalization();           // 2. set culture
app.UseSliceAuthentication();         // 3. UseAuthentication + UseAuthorization
app.UseSliceMultiTenancy();           // 4. resolve tenant (after auth: reads the tenant_id claim)
app.MapControllers();                 // 5. endpoints
// app.MapSliceHub<…>("/hubs/…");     //    SignalR hubs, if used
```

Authentication must precede multi-tenancy (the tenant is often resolved from the `tenant_id` claim),
and exception handling wraps everything. The conditional-requests middleware sits just *inside* the
exception handler so it maps a stale-write concurrency exception to `412` before the generic 500 path.

---

## API versioning & SignalR

- **Versioning:** `builder.Services.AddSliceApiVersioning()` enables version selection via URL segment
  or the `X-Api-Version` header and emits `api-supported-versions`. See
  [Cross-cutting services](cross-cutting-services.md#api-versioning).
- **Real-time:** derive hubs from `SliceHub` (tenant/user-aware) and map them with
  `MapSliceHub<THub>(pattern)`. See [Cross-cutting services](cross-cutting-services.md#signalr).
- **Hypermedia & caching:** add HAL `_links` (content-negotiated, permission-aware) and `ETag`/`If-Match`
  conditional requests without touching controllers. See
  [Hypermedia & HTTP caching](hypermedia-and-caching.md).
- **Minimal APIs:** prefer minimal endpoints (or mix them with controllers)? `ISliceEndpoint` +
  `MapSliceEndpoints` dispatch through the same pipeline and map `Result<T>` → HTTP, with HAL/ETag/versioning/
  OpenAPI parity. See [Minimal APIs](minimal-apis.md).
