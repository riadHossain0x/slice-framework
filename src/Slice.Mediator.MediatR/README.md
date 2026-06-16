# Slice.Mediator.MediatR

> Adapter that makes MediatR the dispatch engine behind Slice's `ISender`, while Slice handlers and ordered behaviors run unchanged.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

This optional package swaps the built-in `Slice.Mediator.Default` engine for **MediatR** without changing any product code. It wraps each Slice request in a single non-generic MediatR request (`SliceRequestEnvelope`), so there is no need to register open-generic MediatR handlers for every Slice request — one envelope handler runs the shared `RequestPipeline` (from `Slice.Mediator.Default`), keeping Slice's handler + ordered-behavior chain identical. Use it when you want MediatR's ecosystem (its own notifications/behaviors, tooling, or an existing MediatR investment) as the underlying dispatcher.

## Dependencies

- **Slice:** `Slice.Mediator`, `Slice.Mediator.Default` (reuses `RequestPipeline`)
- **Third-party:** `MediatR`, `Microsoft.Extensions.DependencyInjection.Abstractions`

## Module & registration

No `SliceModule`. Registration is a single `IServiceCollection` extension on `MediatRMediatorRegistration`:

```csharp
using Slice.Mediator.MediatR;

services.AddSliceMediatorMediatR();
```

This calls `services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(MediatRSender).Assembly))`, then `RemoveAll<ISender>()` and registers `ISender -> MediatRSender` (transient).

### Switching engines

Replace the default engine registration with the MediatR one. Still register your handlers with `AddRequestHandlers(...)`:

```csharp
// Default engine:
// services.AddSliceMediator();

// MediatR engine instead:
services.AddSliceMediatorMediatR();

services.AddRequestHandlers(typeof(GetWeather).Assembly); // Slice handlers, unchanged
```

Because `AddSliceMediatorMediatR()` does `RemoveAll<ISender>()` first, it cleanly overrides any previously registered (e.g. default) `ISender`.

## Key types

| Type | Kind | Description |
|---|---|---|
| `SliceRequestEnvelope` | `sealed class : MediatR.IRequest<object?>` | Non-generic MediatR request carrying any Slice request. Ctor `SliceRequestEnvelope(object request)`; exposes `object Request { get; }`. |
| `SliceRequestEnvelopeHandler` | `sealed class : MediatR.IRequestHandler<SliceRequestEnvelope, object?>` | The single MediatR handler. Ctor `(IServiceProvider serviceProvider)`. `Handle` runs `RequestPipeline.InvokeAsync(envelope.Request, …)`. |
| `MediatRSender` | `sealed class : ISender` | Slice `ISender` backed by MediatR. Ctor `(MediatR.ISender mediator)`. Sends a `SliceRequestEnvelope` and casts the boxed result to `TResponse`. |
| `MediatRMediatorRegistration` | `static class` | DI extension `AddSliceMediatorMediatR()`. |

## Usage

Product code is unaware of the engine — it depends only on Slice's `ISender` / `IRequest<TResponse>`:

```csharp
using Slice.Mediator;

public sealed class WeatherEndpoint(ISender sender)
{
    public Task<string> Get(string city, CancellationToken ct)
        => sender.SendAsync(new GetWeather(city), ct); // dispatched via MediatR under the hood
}
```

## Notes

- **Lifetime:** `ISender -> MediatRSender` is registered **transient**.
- **One MediatR handler total:** all Slice requests flow through `SliceRequestEnvelope` / `SliceRequestEnvelopeHandler`, so no open-generic MediatR handler registration is required.
- **Same chain:** dispatch ultimately calls the boxed `RequestPipeline.InvokeAsync(object, …)` from `Slice.Mediator.Default`, so Slice handlers and `IHasPipelineOrder`-ordered behaviors behave identically to the default engine.
- `MediatRSender.SendAsync` casts the envelope's `object?` result back to `TResponse`; a handler returning the wrong runtime type would surface as an invalid cast.
