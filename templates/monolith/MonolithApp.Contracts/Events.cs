using Slice.Domain.Events;
using Slice.EventBus;

namespace MonolithApp.Contracts;

// The ONLY thing modules share: integration events. No module references another module's internals —
// they communicate purely through these contracts over the in-process distributed event bus.

[DistributedEventName("orders.order-placed")]
public sealed record OrderPlacedEto(Guid OrderId, string Customer, string Sku, int Quantity) : IDistributedEvent;

[DistributedEventName("billing.invoice-created")]
public sealed record InvoiceCreatedEto(Guid InvoiceId, Guid OrderId, decimal Amount) : IDistributedEvent;
