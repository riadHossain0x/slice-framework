# Slice.AspNetCore.ConditionalRequests

> HTTP conditional requests for Slice APIs — `ETag` + `If-None-Match` → `304`, and `If-Match` → `412` optimistic concurrency over EF Core's concurrency token.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

`Slice.AspNetCore.ConditionalRequests` adds two standard HTTP caching/concurrency behaviours:

- **Reads (GET/HEAD):** every successful response gets a strong `ETag`. If the payload exposes a cheap
  version (`IHasResourceVersion`, e.g. an aggregate's `ConcurrencyStamp`) that is used directly;
  otherwise the body is hashed. A matching `If-None-Match` short-circuits to **`304 Not Modified`** with
  no body, saving bandwidth.
- **Writes (PUT/PATCH/DELETE):** a stale `If-Match` precondition becomes **`412 Precondition Failed`**.
  The handler pins the client's `If-Match` onto EF Core's concurrency token; if another writer moved the
  row on, `SaveChanges` raises `DbUpdateConcurrencyException`, which the middleware maps to 412. A handler
  can also throw `PreconditionFailedException` directly.

## Dependencies

- **Slice:** `Slice.AspNetCore`, `Slice.Modularity`
- **Third-party:** `FrameworkReference Microsoft.AspNetCore.App`, `Microsoft.EntityFrameworkCore` (to map the concurrency exception and to apply `If-Match` to the concurrency token)

## Module & registration

Add `SliceConditionalRequestsModule` to your module graph, then add the middleware **after**
`UseSliceExceptionHandling()` so it maps the concurrency exception before the generic 500 handler sees it:

```csharp
[DependsOn(typeof(SliceConditionalRequestsModule))]
public sealed class CrmModule : SliceModule { }
```

```csharp
app.UseSliceExceptionHandling();
app.UseSliceConditionalRequests();   // ETag/304 + If-Match/412
app.UseSliceAuthentication();
app.MapControllers();
```

## Strong ETags from a resource version

Expose a version on the DTO so reads get a cheap validator (no body hashing):

```csharp
public sealed record LeadDto(Guid Id, /* … */ string ConcurrencyStamp) : IHasResourceVersion
{
    [JsonIgnore] public string ResourceVersion => ConcurrencyStamp;
}
```

## Enforcing If-Match on a write

Carry the client's `If-Match` into the command, then pin it onto the tracked entity. EF emits
`WHERE ConcurrencyStamp = @ifMatch`; a stale value matches zero rows → `DbUpdateConcurrencyException` → 412.

```csharp
// controller
[HttpPatch("{id:guid}/status")]
public Task<IActionResult> ChangeStatus(Guid id, [FromBody] ChangeStatusRequest body, CancellationToken ct)
    => SendAsync(new ChangeLeadStatusCommand(id, body.Status, HttpContext.GetIfMatch()), ct);

// handler
var lead = await repository.FindAsync(command.Id, ct);
lead.ChangeStatus(command.Status);
db.Entry(lead).UseIfMatch(command.IfMatch);   // no-op when If-Match is absent
```

## Key types

| Type | Kind | Description |
|---|---|---|
| `IHasResourceVersion` | interface | Opt-in `ResourceVersion` → cheap strong ETag (else content hash). |
| `ConditionalRequestMiddleware` | middleware | Buffers reads to set `ETag` + answer `If-None-Match` (304); maps `DbUpdateConcurrencyException`/`PreconditionFailedException` on writes to 412. |
| `ConditionalRequestOptions` | options | `MaxBodyBytes` — largest response hashed for a content ETag (default 1 MiB). |
| `ResourceVersionResultFilter` | result filter | Sets the version ETag on safe reads before the body is written. Registered globally by the module. |
| `PreconditionFailedException` | exception | Throw from a handler to force a 412. |
| `ConditionalRequest` | static class | `HttpContext.GetIfMatch()`, `HttpContext.SetETag(version)`, `EntityEntry.UseIfMatch(ifMatch)`. |
| `SliceConditionalRequestsModule` | module | `[DependsOn(SliceAspNetCoreModule)]`; registers the version filter + options. |
| `ConditionalRequestMiddlewareExtensions.UseSliceConditionalRequests()` | extension | Adds the middleware. |

## Minimal APIs

The middleware (304 + 412) is endpoint-agnostic and already covers minimal APIs. For the cheap **version**
ETag, add `.WithResourceVersion()` to an endpoint or `.AddResourceVersion()` to a route group (e.g. inside
`MapSliceEndpoints(..., g => g.AddResourceVersion())`); `ResourceVersionEndpointFilter` sets the strong ETag
from a returned `IHasResourceVersion` value on safe reads — the minimal-API counterpart of the MVC
`ResourceVersionResultFilter`.

## Notes

- **Buffering cost:** the content-hash path buffers the response (reads only, capped by `MaxBodyBytes`). Responses that already carry a version ETag skip hashing entirely.
- **`Vary: Accept`** is added so a HAL representation and a plain-JSON representation are cached separately. The version filter skips when HAL is negotiated, keeping each representation's ETag consistent with its bytes.
- **Middleware order matters:** register inside `UseSliceExceptionHandling()` (i.e. after it) so the 412 mapping wins over the generic 500.
- **Weak-tag comparison** is used for `If-None-Match` per RFC 9110 (the `W/` prefix is ignored).
