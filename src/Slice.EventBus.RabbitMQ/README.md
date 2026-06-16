# Slice.EventBus.RabbitMQ

> RabbitMQ transport adapter for Slice distributed events — publishes to a topic exchange and consumes the app queue.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

`Slice.EventBus.RabbitMQ` plugs RabbitMQ into the `Slice.EventBus` transport seam. `RabbitMqEventPublisher` (an `IDistributedEventPublisher`) serializes each distributed event to JSON and publishes it to a durable topic exchange, using the event's wire name as the routing key. `RabbitMqConsumer` (a `BackgroundService`) declares the exchange/queue, binds with `#`, and for every delivery calls `IDistributedEventConsumer.ConsumeAsync` inside a fresh scope — acking on success and nacking (requeue) on failure. `AddSliceRabbitMq(...)` removes the loopback publisher and wires both ends. The adapter is verified against a real broker via Testcontainers.

## Dependencies

- **Slice:** `Slice.EventBus`, `Slice.Core`
- **Third-party:** `RabbitMQ.Client`; `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Hosting.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`; `System.Text.Json` (BCL)

## Module & registration

```csharp
services.AddSliceRabbitMq(o =>
{
    o.ConnectionString = "amqp://guest:guest@localhost:5672";
    o.Exchange = "slice.events";
    o.Queue    = "slice.app";
});
```

`AddSliceRabbitMq` registers `RabbitMqOptions` and `RabbitMqConnection` as singletons, calls `services.RemoveAll<IDistributedEventPublisher>()`, registers `RabbitMqEventPublisher` as the singleton `IDistributedEventPublisher`, and adds `RabbitMqConsumer` as a hosted service. (The `IDistributedEventConsumer` it dispatches to comes from `Slice.EventBus`.)

## Key types

| Type | Kind | Description |
|---|---|---|
| `RabbitMqOptions` | class | `ConnectionString` (default `amqp://guest:guest@localhost:5672`), `Exchange` (default `slice.events`), `Queue` (default `slice.app`). |
| `RabbitMqConnection` | class | `IAsyncDisposable` holding a lazily-opened, gated shared `IConnection`; `GetAsync(ct)` reopens if closed. |
| `RabbitMqEventPublisher` | class | `IDistributedEventPublisher`. Declares the topic exchange (durable) and publishes with routing key = `registry.GetName(...)`, `MessageId = messageId`, `Persistent = true`. |
| `RabbitMqConsumer` | class | `BackgroundService`. Declares exchange + durable queue, binds `#`, consumes with `autoAck: false`; acks on success, nacks (requeue) on exception. |
| `RabbitMqRegistration` | static class | `AddSliceRabbitMq(this IServiceCollection, Action<RabbitMqOptions>)`. |

## Usage

Define and register your integration events and handlers in `Slice.EventBus`, then switch the transport to RabbitMQ:

```csharp
[DistributedEventName("orders.placed.v1")]
public sealed record OrderPlacedEto(Guid OrderId, decimal Total) : IDistributedEvent;

services.AddDistributedEvents(typeof(OrderPlacedEto).Assembly);
services.AddDistributedEventHandlers(typeof(ProjectOrder).Assembly);

services.AddSliceRabbitMq(o => o.ConnectionString = "amqp://guest:guest@rabbit:5672");

// publishing is unchanged — it now goes over RabbitMQ:
await publisher.PublishAsync(new OrderPlacedEto(orderId, total), Guid.NewGuid().ToString(), ct);
```

## Notes

- **Exchange/queue topology:** a durable topic exchange; the consumer's durable queue binds with routing key `#`, so it receives every event published to the exchange. The publish routing key is the event's wire name.
- **Idempotency:** the broker `MessageId` carries the `messageId`; on the consume side it falls back to a new GUID if absent, then defers dedup to the configured `IInboxStore`.
- **Lifetimes:** options, connection, and publisher are singletons; the consumer creates a scope per message. The connection is opened lazily and reused (gated by a `SemaphoreSlim`).
- **Delivery:** manual ack; failures are nacked with `requeue: true`, so a poison message can loop unless an `IInboxStore` / dead-lettering is in place.
- **Verification:** exercised end-to-end against a Testcontainers RabbitMQ broker (`tests/Slice.EventBus.RabbitMQ.Tests`).
