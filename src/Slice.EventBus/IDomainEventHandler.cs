using Slice.Domain.Events;

namespace Slice.EventBus;

/// <summary>Handles a local (in-process) domain event. Many handlers may handle one event.</summary>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent @event, CancellationToken ct);
}

/// <summary>Publishes local domain events to their registered handlers.</summary>
public interface ILocalEventBus
{
    Task PublishAsync(IDomainEvent @event, CancellationToken ct = default);
    Task PublishAsync(IEnumerable<IDomainEvent> events, CancellationToken ct = default);
}
