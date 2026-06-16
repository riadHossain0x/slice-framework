# Slice.ApiVersioning

> One-call API versioning for Slice apps, wrapping `Asp.Versioning` with sensible URL-segment + header defaults.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

`Slice.ApiVersioning` provides a single extension, `AddSliceApiVersioning`, that configures the `Asp.Versioning.Mvc` stack with Slice conventions: a default version of `v1.0` (assumed when the caller specifies none), version reading from either the URL segment (`v{version}`) or the `X-Api-Version` header, and reporting of supported/deprecated versions back in the response. It also wires up MVC versioning via `AddMvc()`.

## Dependencies

- **Slice:** `Slice.Modularity`
- **Third-party:** `Asp.Versioning.Mvc`, `Asp.Versioning.Mvc.ApiExplorer`

## Module & registration

There is no `SliceModule` in this library — registration is a single `IServiceCollection` extension you call during composition.

```csharp
builder.Services.AddSliceApiVersioning();
```

This applies the following `Asp.Versioning` options:

- `DefaultApiVersion = new ApiVersion(1, 0)` — default of v1.0
- `AssumeDefaultVersionWhenUnspecified = true` — unversioned requests fall back to v1.0
- `ReportApiVersions = true` — supported/deprecated versions reported in responses
- `ApiVersionReader = ApiVersionReader.Combine(new UrlSegmentApiVersionReader(), new HeaderApiVersionReader("X-Api-Version"))`

followed by `.AddMvc()`.

## Key types

| Type | Kind | Description |
|---|---|---|
| `SliceApiVersioningRegistration` | Static class | Hosts the registration extension. |
| `SliceApiVersioningRegistration.AddSliceApiVersioning(this IServiceCollection)` | Extension method | Configures `Asp.Versioning` (default v1.0, URL-segment + `X-Api-Version` header readers, version reporting) and `AddMvc()`. Returns the same `IServiceCollection`. |

## Usage

Register during startup:

```csharp
builder.Services.AddSliceApiVersioning();
builder.Services.AddControllers();
```

Version a controller and route the version through the URL segment:

```csharp
using Asp.Versioning;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public sealed class OrdersController : ControllerBase
{
    [HttpGet]
    public IActionResult List() => Ok();
}
```

Callers reach it via the URL segment (`GET /api/v1.0/orders`) or the header (`X-Api-Version: 1.0`). Because reporting is enabled, responses include the supported versions header:

```
api-supported-versions: 1.0
```

## Notes

- No version reader is configured for query strings or media types — only **URL segment** and the **`X-Api-Version` header**.
- Because `AssumeDefaultVersionWhenUnspecified` is `true`, unversioned requests are treated as `1.0`; remove/override this if you want versioning to be mandatory.
- `AddMvc()` (from `Asp.Versioning.Mvc`) is included, so this is geared toward controller-based MVC APIs. For the API explorer / Swagger version grouping, the `Asp.Versioning.Mvc.ApiExplorer` package is referenced and available.
- Deprecated versions (via `[ApiVersion("...", Deprecated = true)]`) are reported in the `api-deprecated-versions` response header thanks to `ReportApiVersions = true`.
