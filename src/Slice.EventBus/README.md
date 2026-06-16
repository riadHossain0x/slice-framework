# Slice.EventBus

> The event seam for Slice: in-process domain events, distributed (integration) events, and the transport-adapter contract that brokers plug into.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

`Slice.EventBus` provides two complementary buses over the marker interfaces in `Slice.Domain.Events`. The **local** bus (`ILocalEventBus` / `LocalEventBus`) dispatches `IDomainEvent`s synchronously to every `IDomainEventHandler<TEvent>` in-process. The **distributed** side carries `IDistributedEvent`s outward through a swappable transport seam: `IDistributedEventPublisher` (default `LocalDistributedEventPublisher`, a loopback) sends, and `IDistributedEventConsumer` (`DistributedEventConsumer`) receives — deduping via an `IInboxStore`, resolving the wire name through `IDistributedEventTypeRegistry`, deserializing, and handing off to local `IDistributedEventHandler<TEvent>`s via the in-process `IDistributedEventBus`. Out of the box everything runs single-process; a transport adapter (RabbitMQ, Kafka, Azure Service Bus) replaces the publisher and adds a consumer pump.

## Dependencies

- **Slice:** `Slice.Core` (DI markers), `Slice.Domain` (`IDomainEvent`, `IDistributedEvent`)
- **Third-party:** `Microsoft.Extensions.DependencyInjection.Abstractions`; `System.Text.Json` (BCL)

## Module & registration

This assembly's default implementations (`LocalEventBus`, `DistributedEventBus`, `LocalDistributedEventPublisher`, `DistributedEventConsumer`, `DistributedEventTypeRegistry`, `NullInboxStore`) carry DI markers (`ISingletonDependency` / `IScopedDependency`), so they are wired automatically when the assembly is scanned by `AddSliceConventions(assembly)` (from `Slice.Modularity`). Three explicit scanning helpers register your own types:

```csharp
// Bus + consumer + registry come from AddSliceConventions(typeof(LocalEventBus).Assembly).

// Register the wire-name registry entries for your distributed events:
services.AddDistributedEvents(typeof(OrderPlacedEto).Assembly);

// Register handler implementations found in an assembly:
services.AddDomainEventHandlers(typeof(MyDomainHandler).Assembly);
services.AddDistributedEventHandlers(typeof(MyIntegrationHandler).Assembly);
```

## Key types

| Type | Kind | Description |
|---|---|---|
| `IDomainEventHandler<TEvent>` | interface | Handles a local `IDomainEvent`; `HandleAsync(TEvent, CancellationToken)`. Many handlers per event. |
| `ILocalEventBus` | interface | Publishes local domain events: `PublishAsync(IDomainEvent, ct)` and `PublishAsync(IEnumerable<IDomainEvent>, ct)`. |
| `LocalEventBus` | class | Default `ILocalEventBus` (`ISingletonDependency`). Resolves handlers for the event's runtime type (cached dispatcher) and invokes them sequentially. |
| `IDistributedEventHandler<TEvent>` | interface | Handles an integration event; `HandleAsync(TEvent, CancellationToken)`. `TEvent : IDistributedEvent`. |
| `IDistributedEventBus` | interface | In-process dispatch of distributed events: `PublishAsync(IDistributedEvent, ct)`. |
| `DistributedEventBus` | class | Default `IDistributedEventBus` (`IScopedDependency`). Resolves `IDistributedEventHandler<TEvent>`s from the current scope. |
| `IDistributedEventPublisher` | interface | **Transport send seam**: `PublishAsync(IDistributedEvent @event, string messageId, ct)`. |
| `LocalDistributedEventPublisher` | class | Default publisher (`IScopedDependency`) — loopbacks to `IDistributedEventBus` (single process). |
| `IDistributedEventConsumer` | interface | **Transport receive seam**: `ConsumeAsync(string eventName, string messageId, byte[] payload, ct)`. |
| `DistributedEventConsumer` | class | Default consumer (`IScopedDependency`): dedup → `registry.ResolveType` → JSON deserialize → `bus.PublishAsync`. |
| `IDistributedEventTypeRegistry` | interface | Maps CLR type ⇄ wire name: `GetName(Type)`, `ResolveType(string)`. |
| `DistributedEventTypeRegistry` | class | Default registry (`ISingletonDependency`) built from injected `DistributedEventRegistration`s; falls back to `Type.FullName`. |
| `DistributedEventNameAttribute` | class | `[DistributedEventName("...")]` — overrides the wire name (default = full type name). Class-only, not inherited. |
| `DistributedEventRegistration` | record | `(Type EventType, string EventName)` — one registry entry. |
| `IInboxStore` | interface | Idempotency gate: `TryMarkProcessedAsync(string messageId, ct)` → `true` if new (proceed), `false` if seen (skip). |
| `NullInboxStore` | class | Default no-dedup store (`ISingletonDependency`); always returns `true`. |
| `InMemoryInboxStore` | class | In-memory dedup (single node / tests) backed by a `ConcurrentDictionary`. |
| `EventBusRegistration` | static class | `AddDomainEventHandlers(assembly)` — scans for closed `IDomainEventHandler<>` (transient). |
| `DistributedEventBusRegistration` | static class | `AddDistributedEventHandlers(assembly)` — scans for closed `IDistributedEventHandler<>` (transient). |
| `DistributedEventRegistrationExtensions` | static class | `AddDistributedEvents(assembly)` — registers `DistributedEventRegistration` per `IDistributedEvent` type, honouring `[DistributedEventName]`. |

## Usage

Define a local domain event and handler:

```csharp
public sealed record OrderPlaced(Guid OrderId) : IDomainEvent;

public sealed class SendReceipt : IDomainEventHandler<OrderPlaced>
{
    public Task HandleAsync(OrderPlaced @event, CancellationToken ct) { /* ... */ return Task.CompletedTask; }
}

// publish
await localEventBus.PublishAsync(new OrderPlaced(orderId), ct);
```

Define a distributed (integration) event with a stable wire name and a handler:

```csharp
[DistributedEventName("orders.placed.v1")]
public sealed record OrderPlacedEto(Guid OrderId, decimal Total) : IDistributedEvent;

public sealed class ProjectOrder : IDistributedEventHandler<OrderPlacedEto>
{
    public Task HandleAsync(OrderPlacedEto @event, CancellationToken ct) { /* ... */ return Task.CompletedTask; }
}

// registration
services.AddDistributedEvents(typeof(OrderPlacedEto).Assembly);
services.AddDistributedEventHandlers(typeof(ProjectOrder).Assembly);

// publish outward through the transport seam (loopback by default)
await publisher.PublishAsync(new OrderPlacedEto(orderId, total), messageId: Guid.NewGuid().ToString(), ct);
```

## Writing a transport adapter

A broker adapter implements exactly two pieces and replaces the loopback:

1. **`IDistributedEventPublisher`** — serialize `@event` (use `registry.GetName(@event.GetType())` for the wire name and `messageId` for idempotency) and send to the broker. Register it after `services.RemoveAll<IDistributedEventPublisher>()`.
2. **A `BackgroundService` / `IHostedService` consumer** — for each broker message, create a scope and call `IDistributedEventConsumer.ConsumeAsync(eventName, messageId, payload, ct)`. The default `DistributedEventConsumer` then handles dedup, type resolution, deserialization, and local dispatch.

The shipped `Slice.EventBus.RabbitMQ`, `Slice.EventBus.Kafka`, and `Slice.EventBus.AzureServiceBus` packages follow this exact pattern.

## Notes

- **Lifetimes:** `LocalEventBus` and `DistributedEventTypeRegistry` / `NullInboxStore` are singletons; `DistributedEventBus`, `LocalDistributedEventPublisher`, and `DistributedEventConsumer` are scoped — consumers resolve them inside a fresh DI scope per message.
- **Dispatch is sequential** within an event: handlers run one after another in registration order (no parallelism, no isolation between handlers).
- **Wire name default** is the event's `Type.FullName`; override with `[DistributedEventName("...")]`. `ResolveType` returns `null` for unknown names — `DistributedEventConsumer` silently ignores them.
- **Default dedup is off** (`NullInboxStore` always says "new"). Swap in `InMemoryInboxStore` for single-node dedup, or a durable store for multi-node exactly-once-ish processing.
- Payloads are serialized with `System.Text.Json` using the concrete runtime type.
