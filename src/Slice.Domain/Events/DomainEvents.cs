namespace Slice.Domain.Events;

/// <summary>A local, in-process event raised by an aggregate. Dispatched around SaveChanges.</summary>
public interface IDomainEvent;

/// <summary>An integration event (ETO) destined for the outbox / other services. Suffix: <c>Eto</c>.</summary>
public interface IDistributedEvent;

/// <summary>Implemented by aggregates that collect domain/distributed events.</summary>
public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }
    IReadOnlyCollection<IDistributedEvent> DistributedEvents { get; }
    void ClearDomainEvents();
    void ClearDistributedEvents();
}
