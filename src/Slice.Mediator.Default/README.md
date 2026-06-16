# Slice.Mediator.Default

> The built-in, reflection-light `ISender` engine: a default sender, a cached request-pipeline executor, and assembly handler scanning.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

This is the in-framework dispatch engine that implements the `ISender` abstraction from `Slice.Mediator`. `DefaultSender` delegates to the static `RequestPipeline`, which resolves a request's handler plus its ordered pipeline behaviors, folds them into a single call chain, and caches the per-request-type wrapper so the only per-request reflection is a dictionary lookup. `RequestPipeline` is public and reusable — the MediatR adapter runs the identical Slice chain through it.

## Dependencies

- **Slice:** `Slice.Mediator`, `Slice.Modularity`
- **Third-party:** `Microsoft.Extensions.DependencyInjection.Abstractions`

## Module & registration

No `SliceModule` here — registration is via two `IServiceCollection` extensions on `MediatorRegistration`:

```csharp
using Slice.Mediator.Default;

services.AddSliceMediator();                    // registers ISender -> DefaultSender (scoped, TryAdd)
services.AddRequestHandlers(typeof(GetWeather).Assembly); // scans + registers handlers (transient)
```

Call `AddRequestHandlers(...)` once per assembly that contains handlers.

## Key types

| Type | Kind | Description |
|---|---|---|
| `DefaultSender` | `sealed class : ISender` | Default sender. Ctor `DefaultSender(IServiceProvider serviceProvider)`. `SendAsync` forwards to `RequestPipeline.InvokeAsync`. |
| `RequestPipeline` | `static class` | Reusable pipeline runner. `Task<TResponse> InvokeAsync<TResponse>(IRequest<TResponse>, IServiceProvider, CancellationToken)` and a boxed `Task<object?> InvokeAsync(object, IServiceProvider, CancellationToken)`. Caches per-type wrappers/invokers in `ConcurrentDictionary`. |
| `MediatorRegistration` | `static class` | DI extensions: `AddSliceMediator()` and `AddRequestHandlers(Assembly)`. |

(`RequestHandlerWrapper<TResponse>` and `RequestHandlerWrapperImpl<TRequest, TResponse>` are internal implementation details of the cached fold.)

## How handlers are discovered and registered

`AddRequestHandlers(assembly)` iterates the assembly's types, skipping anything that is not a concrete class (`IsClass: true, IsAbstract: false`). For each remaining type it finds every implemented closed generic of `IRequestHandler<,>` and registers the type against that handler interface as **transient**:

```csharp
services.AddTransient(handlerInterface, type);
```

So a `class GetWeatherHandler : IRequestHandler<GetWeather, string>` is registered as `IRequestHandler<GetWeather, string> -> GetWeatherHandler`.

At dispatch time, `RequestHandlerWrapperImpl<TRequest, TResponse>` resolves the handler with `GetRequiredService<IRequestHandler<TRequest, TResponse>>()`, then builds the behavior chain by resolving all `IPipelineBehavior<TRequest, TResponse>` services, ordering them by ascending `(b as IHasPipelineOrder)?.Order ?? PipelineOrder.Default`, reversing, and `Aggregate`-folding them around the handler call.

## Usage

```csharp
using Microsoft.Extensions.DependencyInjection;
using Slice.Mediator;
using Slice.Mediator.Default;

var services = new ServiceCollection();

services.AddSliceMediator();                       // ISender -> DefaultSender
services.AddRequestHandlers(typeof(GetWeather).Assembly);

var provider = services.BuildServiceProvider();

using var scope = provider.CreateScope();
var sender = scope.ServiceProvider.GetRequiredService<ISender>();
var forecast = await sender.SendAsync(new GetWeather("London"));
```

## Notes

- **Lifetimes:** `ISender` is registered **scoped** via `TryAddScoped` (won't overwrite a previously registered `ISender`). Handlers are registered **transient**.
- **Caching:** the per-request-type wrapper (and the boxed invoker) are cached in `ConcurrentDictionary`, so steady-state dispatch does no per-request reflection beyond a dictionary lookup.
- **Engine seam:** `RequestPipeline` is shared — `Slice.Mediator.MediatR` reuses the boxed `InvokeAsync(object, …)` entry to run the same Slice chain under MediatR.
- The boxed entry throws `InvalidOperationException` if the supplied object does not implement `IRequest<TResponse>`.
