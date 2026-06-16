# Slice.EventBus.Kafka

> Kafka transport adapter for Slice distributed events — publishes to a topic and runs a polling consumer, built on Slice.Kafka.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

`Slice.EventBus.Kafka` plugs Kafka into the `Slice.EventBus` transport seam, reusing the producer pool and consumer factory from `Slice.Kafka`. `KafkaEventPublisher` (an `IDistributedEventPublisher`) serializes each distributed event to JSON, sets the record key to the event's wire name, and carries the wire name and message id in headers. `KafkaConsumer` (a `BackgroundService`) runs Confluent's blocking poll loop off the host startup thread, reads those headers, and per record calls `IDistributedEventConsumer.ConsumeAsync` inside a fresh scope. `AddSliceKafkaEventBus(...)` wires `Slice.Kafka`, removes the loopback publisher, and registers both ends. The adapter is verified end-to-end against a real Testcontainers Kafka broker.

## Dependencies

- **Slice:** `Slice.EventBus`, `Slice.Kafka`, `Slice.Core`
- **Third-party:** `Confluent.Kafka` (via `Slice.Kafka`); `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Hosting.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`; `System.Text.Json` (BCL)

## Module & registration

```csharp
services.AddSliceKafkaEventBus(
    connection: c => c.BootstrapServers = "localhost:9092",
    bus: b =>
    {
        b.Topic   = "slice-events";
        b.GroupId = "slice-app";
    });
```

`AddSliceKafkaEventBus(Action<KafkaConnectionOptions> connection, Action<KafkaEventBusOptions>? bus = null)` calls `AddSliceKafka(connection)`, registers `KafkaEventBusOptions` (singleton), calls `services.RemoveAll<IDistributedEventPublisher>()`, registers `KafkaEventPublisher` as the singleton `IDistributedEventPublisher`, and adds `KafkaConsumer` as a hosted service. The `bus` callback is optional — omit it for defaults.

## Key types

| Type | Kind | Description |
|---|---|---|
| `KafkaEventBusOptions` | class | `Topic` (default `slice-events`), `GroupId` (default `slice-app`). |
| `KafkaEventPublisher` | class | `IDistributedEventPublisher`. Produces a `Message<string, byte[]>` with `Key` = wire name, JSON body, and headers. Constants: `EventNameHeader = "slice-event-name"`, `MessageIdHeader = "slice-message-id"`. |
| `KafkaConsumer` | class | `BackgroundService`. Runs the poll loop via `Task.Run`; consumes with a 500ms timeout, reads event name/id from headers (falling back to the record key / new GUID), and dispatches to `IDistributedEventConsumer`. |
| `KafkaEventBusRegistration` | static class | `AddSliceKafkaEventBus(this IServiceCollection, Action<KafkaConnectionOptions>, Action<KafkaEventBusOptions>?)`. |

## Usage

Define and register your integration events and handlers in `Slice.EventBus`, then switch the transport to Kafka:

```csharp
[DistributedEventName("orders.placed.v1")]
public sealed record OrderPlacedEto(Guid OrderId, decimal Total) : IDistributedEvent;

services.AddDistributedEvents(typeof(OrderPlacedEto).Assembly);
services.AddDistributedEventHandlers(typeof(ProjectOrder).Assembly);

services.AddSliceKafkaEventBus(
    connection: c => c.BootstrapServers = "kafka:9092",
    bus: b => b.GroupId = "orders-service");

// publishing is unchanged — it now goes over Kafka:
await publisher.PublishAsync(new OrderPlacedEto(orderId, total), Guid.NewGuid().ToString(), ct);
```

## Notes

- **Wire name** is both the record `Key` (for partitioning) and the `slice-event-name` header; `messageId` rides in the `slice-message-id` header. On consume, the header wins, falling back to the record key (name) or a new GUID (id).
- **Poll loop:** `Consume(500ms)` runs on a background `Task`; `ConsumeException`s are logged and skipped; dispatch runs synchronously via `GetAwaiter().GetResult()`. The consumer `Close()`s on shutdown (`OperationCanceledException` is treated as graceful).
- **Offsets:** consumers use `AutoOffsetReset.Earliest` and auto-commit (from `Slice.Kafka`'s `KafkaConsumerFactory`); dedup is deferred to the configured `IInboxStore`.
- **Lifetimes:** options and publisher are singletons (producer is the shared pooled instance); the consumer creates a DI scope per record.
- **Verification:** exercised end-to-end against a Testcontainers Kafka broker (`tests/Slice.EventBus.Kafka.Tests`).
