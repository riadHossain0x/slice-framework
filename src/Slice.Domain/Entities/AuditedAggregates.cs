using Slice.Domain.Auditing;

namespace Slice.Domain.Entities;

/// <summary>Aggregate root with creation audit fields (set by the persistence interceptors).</summary>
public abstract class CreationAuditedAggregateRoot<TKey> : AggregateRoot<TKey>, ICreationAuditedObject
{
    protected CreationAuditedAggregateRoot() { }
    protected CreationAuditedAggregateRoot(TKey id) : base(id) { }

    public DateTime CreationTime { get; set; }
    public Guid? CreatorId { get; set; }
}

/// <summary>Adds modification audit fields.</summary>
public abstract class AuditedAggregateRoot<TKey> : CreationAuditedAggregateRoot<TKey>, IAuditedObject
{
    protected AuditedAggregateRoot() { }
    protected AuditedAggregateRoot(TKey id) : base(id) { }

    public DateTime? LastModificationTime { get; set; }
    public Guid? LastModifierId { get; set; }
}

/// <summary>Adds soft-delete + deletion audit fields. The ABP-equivalent base aggregate.</summary>
public abstract class FullAuditedAggregateRoot<TKey> : AuditedAggregateRoot<TKey>, IFullAuditedObject
{
    protected FullAuditedAggregateRoot() { }
    protected FullAuditedAggregateRoot(TKey id) : base(id) { }

    public bool IsDeleted { get; set; }
    public DateTime? DeletionTime { get; set; }
    public Guid? DeleterId { get; set; }
}
