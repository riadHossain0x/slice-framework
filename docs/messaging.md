# Messaging & events

Slice has two event channels, both raised from aggregates and dispatched by the unit of work:

- **Domain events** (`IDomainEvent`) — *in-process*. Side effects within the same service/transaction
  boundary. Dispatched to `IDomainEventHandler<T>` after `SaveChanges`.
- **Distributed (integration) events** (`IDistributedEvent`) — *cross-process*. Written to the
  transactional outbox in the business transaction, then published through a pluggable transport to
  other services.

Packages: `Slice.EventBus` (the buses, the transport seam, the registry, the inbox),
`Slice.EventBus.RabbitMQ` / `Slice.EventBus.AzureServiceBus` / `Slice.EventBus.Kafka` (transports),
`Slice.Kafka` (Kafka client).

---

## Raising events from an aggregate

```csharp
public sealed class Lead : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public void Convert()
    {
        Status = LeadStatus.Converted;
        AddDomainEvent(new LeadConvertedEvent(Id));               // in-process
        AddDistributedEvent(new LeadConvertedEto(Id, TenantId));  // integration → outbox
    }
}
```

`AddDomainEvent` / `AddDistributedEvent` are protected methods on `AggregateRoot<T>`. The
`DomainEventInterceptor` collects them at `SaveChanges`: distributed events become `SliceOutbox` rows
in the same transaction; domain events dispatch in-process after the save.

---

## Domain events (in-process)

```csharp
public sealed record LeadConvertedEvent(Guid LeadId) : IDomainEvent;

public sealed class SendWelcomeEmailOnConvert(IEmailSender email) : IDomainEventHandler<LeadConvertedEvent>
{
    public Task HandleAsync(LeadConvertedEvent e, CancellationToken ct) => /* … */;
}
```

Register handlers with `AddDomainEventHandlers(assembly)` in your module. They run via `ILocalEventBus`
after the transaction commits.

---

## Distributed events (cross-process)

### Naming events

Give every integration event a stable wire name so brokers and consumers agree on it across services:

```csharp
[DistributedEventName("crm.lead-converted")]
public sealed record LeadConvertedEto(Guid LeadId, Guid? TenantId) : IDistributedEvent;
```

Without the attribute the wire name defaults to the type's `FullName`. `IDistributedEventTypeRegistry`
maps names ⇄ types; populate it with `AddDistributedEvents(assembly)`.

### Handlers

```csharp
public sealed class ProjectLeadConverted : IDistributedEventHandler<LeadConvertedEto>
{
    public Task HandleAsync(LeadConvertedEto e, CancellationToken ct) => /* update a read model */;
}
```

Register with `AddDistributedEventHandlers(assembly)`.

### The outbox → transport flow

```
Handler mutates aggregate, returns success
        │
        ▼  UnitOfWorkBehavior → SaveChangesAsync (ONE transaction)
   business rows + SliceOutbox rows        ← DomainEventInterceptor serialized the IDistributedEvents
        │
        ▼  OutboxProcessor<TContext> (hosted; polls; holds an IDistributedLock so only one node sends)
   IDistributedEventPublisher.PublishAsync(event, messageId)
        │
        ├─ default: LocalDistributedEventPublisher → in-process dispatch (single service / dev)
        └─ adapter: RabbitMQ / Azure Service Bus / Kafka → broker
                                   │
                                   ▼ (consuming service)
                 transport consumer → IDistributedEventConsumer.ConsumeAsync(eventName, messageId, payload)
                                   │
                                   ├─ IInboxStore.TryMarkProcessedAsync(messageId)  → skip duplicates
                                   ├─ registry resolves the type, payload deserialized
                                   └─ IDistributedEventBus dispatches to IDistributedEventHandler<T>
```

The **outbox** guarantees an event persists iff the business change does. The **inbox**
(`IInboxStore`) guarantees at-most-once *handling* on the consumer: `NullInboxStore` (no dedup,
default), `InMemoryInboxStore` (single node/tests), or the EF `EfInboxStore` (`SliceInbox` table) for
durable dedup.

---

## Choosing a transport

The default is local loopback (everything in one process). Switch by adding a transport — each calls
`RemoveAll<IDistributedEventPublisher>()`, registers its publisher as a singleton, and runs a
`BackgroundService` consumer.

### RabbitMQ

```csharp
services.AddSliceRabbitMq(o => o.ConnectionString = "amqp://guest:guest@localhost:5672");
// o.Exchange = "slice.events" (topic), o.Queue = "slice.app"
```

Topic exchange; the consumer binds `#`, acks on success, nacks-with-requeue on failure.

### Azure Service Bus

```csharp
services.AddSliceAzureServiceBus(o => { o.ConnectionString = "<connection>"; /* o.Topic, o.Subscription */ });
```

Publishes to a topic with `Subject` = the event wire name; the consumer processes a subscription.

### Kafka

`Slice.Kafka` provides the client (producer pool + consumer factory); `Slice.EventBus.Kafka` the
transport on top:

```csharp
services.AddSliceKafkaEventBus(
    connection: o => o.BootstrapServers = "localhost:9092",
    bus:        o => { o.Topic = "slice-events"; o.GroupId = "slice-app"; });
```

The publisher keys each record by wire name and carries `slice-event-name` / `slice-message-id`
headers. The consumer polls with `AutoOffsetReset.Earliest` and dispatches via
`IDistributedEventConsumer`.

---

## Writing your own transport

A transport adapter implements exactly two pieces against the seam in `Slice.EventBus`:

1. **`IDistributedEventPublisher`** — serialize the event and send it (key/subject = the wire name from
   `IDistributedEventTypeRegistry.GetName`, carry the `messageId`).
2. **A `BackgroundService` consumer** that, for each received message, opens a scope, resolves
   `IDistributedEventConsumer`, and calls
   `ConsumeAsync(eventName, messageId, payload, ct)` — the framework handles dedup, type resolution,
   deserialization, and handler dispatch.

Register both with `RemoveAll<IDistributedEventPublisher>()` + `AddSingleton<IDistributedEventPublisher, …>()`
+ `AddHostedService<…Consumer>()` behind an `AddSlice…` extension. (The RabbitMQ and Kafka transports
are verified end-to-end against real brokers via Testcontainers — see [Testing](testing.md).)
