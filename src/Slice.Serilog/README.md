# Slice.Serilog

> Serilog wiring for Slice apps — configuration-driven logging plus per-request enrichment with the current tenant and user.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

`Slice.Serilog` provides two host/pipeline extensions. `UseSliceSerilog<T>` builds the global `Log.Logger` from configuration (sinks, levels) with `LogContext` enrichment and routes ASP.NET Core logging through Serilog. `UseSliceSerilogRequestLogging` adds Serilog HTTP request logging and pushes `TenantId`/`UserId` (from Slice's `ICurrentTenant`/`ICurrentUser` ambient services) into `LogContext` so every log written during a request is tenant- and user-stamped.

## Dependencies

- **Slice:** `Slice.Core` (uses `ICurrentTenant` / `ICurrentUser` from `Slice.Core.Ambient`)
- **Third-party:** `Serilog.AspNetCore`; `FrameworkReference Microsoft.AspNetCore.App`

## Module & registration

There is no `SliceModule` here — wiring is done through host-builder and pipeline extensions.

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.UseSliceSerilog(); // reads Serilog config + enriches from LogContext

var app = builder.Build();

app.UseSliceSerilogRequestLogging(); // place early in the pipeline

app.MapControllers();
app.Run();
```

`UseSliceSerilog`:

1. `new LoggerConfiguration().ReadFrom.Configuration(builder.Configuration).Enrich.FromLogContext()`
2. invokes your optional `configure` callback
3. assigns `Log.Logger = loggerConfiguration.CreateLogger()`
4. `builder.Logging.ClearProviders()`
5. `builder.Logging.AddSerilog(Log.Logger, dispose: true)`

## Key types

| Type | Kind | Description |
|---|---|---|
| `SliceSerilogExtensions` | Static class | Hosts both extensions below. |
| `UseSliceSerilog<T>(this T builder, Action<LoggerConfiguration>? configure = null)` where `T : IHostApplicationBuilder` | Extension method | Builds `Log.Logger` from configuration + `Enrich.FromLogContext()`, lets you extend the configuration, clears existing providers, and adds Serilog (`dispose: true`). Returns the builder. |
| `UseSliceSerilogRequestLogging(this IApplicationBuilder app)` | Extension method | Middleware that pushes `TenantId` (`ICurrentTenant.Id`) and `UserId` (`ICurrentUser.Id`) onto `LogContext`, then calls `UseSerilogRequestLogging()`. Returns the app. |

## Usage

Extend the logger configuration inline (e.g. add a sink) via the `configure` callback:

```csharp
builder.UseSliceSerilog(lc => lc
    .MinimumLevel.Information()
    .WriteTo.Console());
```

With request logging enabled, any log written during a request automatically carries the tenant/user:

```csharp
public sealed class OrdersController(ILogger<OrdersController> logger) : ControllerBase
{
    [HttpPost]
    public IActionResult Place()
    {
        // This entry is enriched with TenantId + UserId from LogContext.
        logger.LogInformation("Order placed");
        return Ok();
    }
}
```

## Notes

- **`ReadFrom` caveat:** this library uses the `Log.Logger` pattern (`ReadFrom.Configuration(...)` + `CreateLogger()` assigned to the static `Log.Logger`), **not** the `(sp, lc) => ...` / `ReadFrom.Services` overload. Sinks/enrichers needing DI services should be configured against `Log.Logger`'s configuration, not resolved from the container at logger-build time.
- Serilog is added with `dispose: true`, so the logger is flushed/disposed when the host shuts down.
- `UseSliceSerilogRequestLogging` resolves `ICurrentTenant`/`ICurrentUser` per request via `context.RequestServices.GetService<...>()`; if absent (`null`), the corresponding `LogContext` property is simply pushed as null.
- Place `UseSliceSerilogRequestLogging` **early** in the pipeline so downstream logs inherit the enriched context.
- **Testing gotcha:** when asserting against the Serilog InMemory sink, do **not** call `Log.CloseAndFlush()` before reading the captured events, or the events may be lost before your assertions run.
