# Slice.Kafka

> Thin Kafka client abstraction for Slice — shared connection options, a pooled producer, and a consumer factory over Confluent.Kafka.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

`Slice.Kafka` wraps `Confluent.Kafka` with the minimal pieces a Slice app needs to talk to Kafka, independent of the event bus. `KafkaConnectionOptions` holds the bootstrap servers plus a `ConfigureClient` hook applied to every producer/consumer config. `IKafkaProducerPool` / `KafkaProducerPool` exposes a single lazily-created, thread-safe `IProducer<string, byte[]>` reused across publishes. `IKafkaConsumerFactory` / `KafkaConsumerFactory` builds configured `IConsumer<string, byte[]>` instances for a given group. `AddSliceKafka(...)` registers all three. `Slice.EventBus.Kafka` builds the distributed-event transport on top of this package.

## Dependencies

- **Slice:** `Slice.Core`
- **Third-party:** `Confluent.Kafka`; `Microsoft.Extensions.DependencyInjection.Abstractions`

## Module & registration

```csharp
services.AddSliceKafka(o =>
{
    o.BootstrapServers = "localhost:9092";
    o.ConfigureClient = c =>
    {
        c.SecurityProtocol = SecurityProtocol.SaslSsl;
        // acks, sasl, etc.
    };
});
```

`AddSliceKafka` registers `KafkaConnectionOptions` (singleton), `IKafkaProducerPool` → `KafkaProducerPool` (singleton), and `IKafkaConsumerFactory` → `KafkaConsumerFactory` (singleton).

## Key types

| Type | Kind | Description |
|---|---|---|
| `KafkaConnectionOptions` | class | `BootstrapServers` (default `localhost:9092`) and `Action<ClientConfig>? ConfigureClient`. Internal `Apply(ClientConfig)` sets bootstrap servers then runs the hook. |
| `IKafkaProducerPool` | interface | `IProducer<string, byte[]> Get()` — the shared producer. |
| `KafkaProducerPool` | class | Default pool (`IDisposable`). Lazily builds one `IProducer<string, byte[]>`; `Dispose` flushes (5s) and disposes if created. |
| `IKafkaConsumerFactory` | interface | `IConsumer<string, byte[]> Create(string groupId)`. |
| `KafkaConsumerFactory` | class | Default factory. Builds a consumer with `AutoOffsetReset.Earliest`, `EnableAutoCommit = true`, then applies `ConfigureClient`. |
| `SliceKafkaRegistration` | static class | `AddSliceKafka(this IServiceCollection, Action<KafkaConnectionOptions>)`. |

## Usage

```csharp
// produce
var producer = producerPool.Get();
await producer.ProduceAsync("my-topic",
    new Message<string, byte[]> { Key = "k", Value = payload }, ct);

// consume
using var consumer = consumerFactory.Create("my-group");
consumer.Subscribe("my-topic");
var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
```

## Notes

- **Lifetimes:** options, pool, and factory are all singletons. The producer is shared and thread-safe; consumers are created per call and owned by the caller (dispose / `Close()` them).
- **Defaults:** consumers read from `Earliest` with auto-commit on. Producers use a default `ProducerConfig` plus your `ConfigureClient` hook.
- `ConfigureClient` is the single place to layer security (SASL/SSL), acks, and other Confluent settings onto both producers and consumers.
- This package has no dependency on `Slice.EventBus`; it is a standalone Kafka client abstraction.
