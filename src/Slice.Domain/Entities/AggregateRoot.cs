using Slice.Domain.Auditing;
using Slice.Domain.Events;

namespace Slice.Domain.Entities;

public interface IAggregateRoot : IEntity;
public interface IAggregateRoot<out TKey> : IEntity<TKey>, IAggregateRoot;

/// <summary>
/// Base aggregate root. Collects local/distributed events and carries a concurrency stamp.
/// All state changes should go through business methods — never public setters.
/// </summary>
public abstract class AggregateRoot<TKey> : Entity<TKey>, IAggregateRoot<TKey>, IHasDomainEvents, IHasConcurrencyStamp, IHasExtraProperties
{
    protected AggregateRoot() { }
    protected AggregateRoot(TKey id) : base(id) { }

    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Schema-less extra data persisted as a single JSON column. Use the <c>GetProperty</c>/<c>SetProperty</c> extensions.</summary>
    public ExtraPropertyDictionary ExtraProperties { get; private set; } = new();

    private readonly List<IDomainEvent> _domainEvents = [];
    private readonly List<IDistributedEvent> _distributedEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public IReadOnlyCollection<IDistributedEvent> DistributedEvents => _distributedEvents.AsReadOnly();

    protected void AddDomainEvent(IDomainEvent @event) => _domainEvents.Add(@event);
    protected void AddDistributedEvent(IDistributedEvent @event) => _distributedEvents.Add(@event);

    public void ClearDomainEvents() => _domainEvents.Clear();
    public void ClearDistributedEvents() => _distributedEvents.Clear();
}
