# Slice.Mediator

> Engine-agnostic mediator abstractions (`ISender`, requests, handlers, ordered pipeline behaviors) plus the shared request-pipeline contract.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

This library is the abstraction layer for in-process request dispatch. It defines what a request, a handler, and a cross-cutting pipeline behavior look like — without committing to any concrete dispatch engine. Product code depends only on these contracts; a separate engine package (`Slice.Mediator.Default` or `Slice.Mediator.MediatR`) provides the actual `ISender` implementation. The headline feature is a stable, registration-order-independent way to order pipeline behaviors via `IHasPipelineOrder` and the `PipelineOrder` constants.

## Dependencies

- **Slice:** `Slice.Core`
- **Third-party:** none

## Module & registration

This package contains no `SliceModule` and no DI extension — it is pure abstractions. Registration of an `ISender` engine and of behaviors is done by the engine packages and by `Slice.Application`. Reference it from any layer that needs to declare requests, handlers, or behaviors.

## Key types

| Type | Kind | Description |
|---|---|---|
| `Unit` | `readonly record struct` | Void-response marker for commands that return nothing meaningful. `Unit.Value` is the singleton. |
| `IRequest<TResponse>` | interface | A request (command or query) producing a `TResponse`. Covariant (`out TResponse`). |
| `IRequest` | interface | A request producing no value; shorthand for `IRequest<Unit>`. |
| `IRequestHandler<TRequest, TResponse>` | interface | Handles one request type. Exactly one handler per request. `Task<TResponse> HandleAsync(TRequest request, CancellationToken ct)`. |
| `IRequestHandler<TRequest>` | interface | Convenience base for void handlers; `IRequestHandler<TRequest, Unit>`. |
| `RequestHandlerDelegate<TResponse>` | delegate | `Task<TResponse>()` — the continuation invoking the next behavior (or the handler). |
| `IPipelineBehavior<TRequest, TResponse>` | interface | Wraps request handling with cross-cutting logic. `Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)`. |
| `ISender` | interface | The engine seam. `Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)`. |
| `IHasPipelineOrder` | interface | Optional ordering hint for behaviors; `int Order { get; }`. |
| `PipelineOrder` | static class | Canonical numeric orders for the framework's standard behaviors. |

## Pipeline ordering mechanism

Behaviors are chained by **ascending `Order` (lowest = outermost)**. A behavior implements `IHasPipelineOrder` to declare its position; behaviors that do **not** implement it run **innermost** (just before the handler). This makes cross-module registration order irrelevant — only the `Order` value matters.

`PipelineOrder` defines the canonical positions (outermost → innermost):

| Constant | Value |
|---|---|
| `PipelineOrder.Logging` | `100` |
| `PipelineOrder.MultiTenancy` | `200` |
| `PipelineOrder.Authorization` | `300` |
| `PipelineOrder.FeatureCheck` | `350` |
| `PipelineOrder.Validation` | `400` |
| `PipelineOrder.UnitOfWork` | `500` |
| `PipelineOrder.Default` | `int.MaxValue` (unordered behaviors run innermost) |

## Usage

Define a request and its handler:

```csharp
using Slice.Mediator;

public sealed record GetWeather(string City) : IRequest<string>;

public sealed class GetWeatherHandler : IRequestHandler<GetWeather, string>
{
    public Task<string> HandleAsync(GetWeather request, CancellationToken ct)
        => Task.FromResult($"Sunny in {request.City}");
}
```

Dispatch through `ISender` (engine injected by a separate package):

```csharp
public sealed class WeatherEndpoint(ISender sender)
{
    public Task<string> Get(string city, CancellationToken ct)
        => sender.SendAsync(new GetWeather(city), ct);
}
```

Write an ordered cross-cutting behavior:

```csharp
public sealed class MyBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>, IHasPipelineOrder
    where TRequest : IRequest<TResponse>
{
    public int Order => PipelineOrder.Authorization; // 300

    public async Task<TResponse> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        // ...pre-processing...
        var response = await next();
        // ...post-processing...
        return response;
    }
}
```

## Notes

- **Abstractions only** — no engine, no DI helpers, no third-party dependencies. The concrete `ISender` is supplied by `Slice.Mediator.Default` or `Slice.Mediator.MediatR`.
- **One handler per request type.** Behaviors, by contrast, are many and composed into a chain.
- **`Order` is the single source of truth** for chain position; registration order is not. Omit `IHasPipelineOrder` to run innermost (`PipelineOrder.Default == int.MaxValue`).
- `IRequest<out TResponse>` is covariant; `Unit` is the canonical "no result" response.
