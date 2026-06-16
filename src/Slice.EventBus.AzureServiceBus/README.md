# Slice.EventBus.AzureServiceBus

> Azure Service Bus transport adapter for Slice distributed events — publishes to a topic and processes a subscription.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

`Slice.EventBus.AzureServiceBus` plugs Azure Service Bus into the `Slice.EventBus` transport seam. `AzureServiceBusEventPublisher` (an `IDistributedEventPublisher`) serializes each distributed event to JSON and sends it to a topic, carrying the event's wire name in the message `Subject` and the `messageId` in `MessageId`. `AzureServiceBusConsumer` (an `IHostedService`) starts a `ServiceBusProcessor` over a subscription and, per message, calls `IDistributedEventConsumer.ConsumeAsync` inside a fresh scope, then completes the message. `AddSliceAzureServiceBus(...)` removes the loopback publisher and wires both ends plus the `ServiceBusClient`.

## Dependencies

- **Slice:** `Slice.EventBus`, `Slice.Core`
- **Third-party:** `Azure.Messaging.ServiceBus`; `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Hosting.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`; `System.Text.Json` (BCL)

## Module & registration

```csharp
services.AddSliceAzureServiceBus(o =>
{
    o.ConnectionString = "Endpoint=sb://...;SharedAccessKeyName=...;SharedAccessKey=...";
    o.Topic        = "slice-events";
    o.Subscription = "slice-app";
});
```

`AddSliceAzureServiceBus` registers `AzureServiceBusOptions` and a `ServiceBusClient` (built from the connection string) as singletons, calls `services.RemoveAll<IDistributedEventPublisher>()`, registers `AzureServiceBusEventPublisher` as the singleton `IDistributedEventPublisher`, and adds `AzureServiceBusConsumer` as a hosted service.

## Key types

| Type | Kind | Description |
|---|---|---|
| `AzureServiceBusOptions` | class | `ConnectionString` (`required`), `Topic` (default `slice-events`), `Subscription` (default `slice-app`). |
| `AzureServiceBusEventPublisher` | class | `IDistributedEventPublisher`. Creates a sender for the topic and sends a `ServiceBusMessage` with `MessageId = messageId` and `Subject = registry.GetName(...)`. |
| `AzureServiceBusConsumer` | class | `IHostedService`. Runs a `ServiceBusProcessor` over `Topic`/`Subscription`; per message dispatches to `IDistributedEventConsumer` then completes; logs process errors. |
| `AzureServiceBusRegistration` | static class | `AddSliceAzureServiceBus(this IServiceCollection, Action<AzureServiceBusOptions>)`. |

## Usage

Define and register your integration events and handlers in `Slice.EventBus`, then switch the transport to Azure Service Bus:

```csharp
[DistributedEventName("orders.placed.v1")]
public sealed record OrderPlacedEto(Guid OrderId, decimal Total) : IDistributedEvent;

services.AddDistributedEvents(typeof(OrderPlacedEto).Assembly);
services.AddDistributedEventHandlers(typeof(ProjectOrder).Assembly);

services.AddSliceAzureServiceBus(o =>
{
    o.ConnectionString = builder.Configuration["ServiceBus:ConnectionString"]!;
});

// publishing is unchanged — it now goes over Azure Service Bus:
await publisher.PublishAsync(new OrderPlacedEto(orderId, total), Guid.NewGuid().ToString(), ct);
```

## Notes

- **Wire name** travels in the message `Subject`; the consumer passes `args.Message.Subject` as the event name and `args.Message.MessageId` as the message id to `IDistributedEventConsumer`.
- **Topology is not created by this adapter** — the `Topic` and `Subscription` must already exist in the namespace.
- **Lifetimes:** options, `ServiceBusClient`, and publisher are singletons; the processor handler creates a DI scope per message.
- **Delivery:** the consumer completes the message after dispatch; the default `ServiceBusProcessorOptions` are used (no custom prefetch/concurrency). Dedup is deferred to the configured `IInboxStore`.
- The publisher creates a sender per publish (`await using`); for high throughput consider this when reading the code.
