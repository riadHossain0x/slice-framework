namespace Slice.Domain.Entities;

public interface IEntity
{
    object?[] GetKeys();
}

public interface IEntity<out TKey> : IEntity
{
    TKey Id { get; }
}

/// <summary>Base class for entities with a single primary key. Identity-based equality.</summary>
public abstract class Entity<TKey> : IEntity<TKey>
{
    protected Entity() { }
    protected Entity(TKey id) => Id = id;

    public TKey Id { get; protected set; } = default!;

    public object?[] GetKeys() => [Id];

    public bool IsTransient() => EqualityComparer<TKey>.Default.Equals(Id, default!);

    public override bool Equals(object? obj)
    {
        if (obj is not Entity<TKey> other || other.GetType() != GetType())
            return false;
        if (IsTransient() || other.IsTransient())
            return ReferenceEquals(this, other);
        return EqualityComparer<TKey>.Default.Equals(Id, other.Id);
    }

    public override int GetHashCode() => IsTransient() ? base.GetHashCode() : HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity<TKey>? a, Entity<TKey>? b) => Equals(a, b);
    public static bool operator !=(Entity<TKey>? a, Entity<TKey>? b) => !Equals(a, b);

    public override string ToString() => $"[{GetType().Name} {Id}]";
}
