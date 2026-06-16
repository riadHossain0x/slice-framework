using System.Linq.Expressions;
using Slice.Domain.Entities;
using Slice.Domain.Events;
using Slice.Domain.Exceptions;
using Slice.Domain.Guards;
using Slice.Domain.Specifications;
using Slice.Domain.Values;

namespace Slice.Domain.Tests;

// ── Fixtures ─────────────────────────────────────────────────────────────────
file sealed class TestEntity(Guid id) : Entity<Guid>(id);

file sealed class Money(decimal amount, string currency) : ValueObject
{
    public decimal Amount { get; } = amount;
    public string Currency { get; } = currency;
    protected override IEnumerable<object?> GetEqualityComponents() { yield return Amount; yield return Currency; }
}

file sealed record ThingHappened(int N) : IDomainEvent;
file sealed record ThingHappenedEto(int N) : IDistributedEvent;

file sealed class TestAggregate : AggregateRoot<Guid>
{
    public TestAggregate(Guid id) : base(id) { }
    public void Raise() { AddDomainEvent(new ThingHappened(1)); AddDistributedEvent(new ThingHappenedEto(1)); }
}

file sealed class IsActive(bool active) : Specification<bool>
{
    public override Expression<Func<bool, bool>> ToExpression() => x => x == active;
}

// ── Tests ────────────────────────────────────────────────────────────────────
public class EntityTests
{
    [Fact]
    public void Same_id_means_equal()
    {
        var id = Guid.NewGuid();
        Assert.Equal(new TestEntity(id), new TestEntity(id));
        Assert.True(new TestEntity(id) == new TestEntity(id));
    }

    [Fact]
    public void Different_id_means_not_equal()
        => Assert.NotEqual(new TestEntity(Guid.NewGuid()), new TestEntity(Guid.NewGuid()));

    [Fact]
    public void Transient_entities_use_reference_equality()
    {
        var a = new TestEntity(Guid.Empty);
        Assert.True(a.IsTransient());
        Assert.NotEqual(a, new TestEntity(Guid.Empty));
        Assert.Equal(a, a);
    }
}

public class ValueObjectTests
{
    [Fact]
    public void Equal_when_components_match()
        => Assert.Equal(new Money(10m, "GBP"), new Money(10m, "GBP"));

    [Fact]
    public void Not_equal_when_a_component_differs()
        => Assert.NotEqual(new Money(10m, "GBP"), new Money(10m, "USD"));
}

public class AggregateRootTests
{
    [Fact]
    public void Collects_and_clears_events_independently()
    {
        var agg = new TestAggregate(Guid.NewGuid());
        agg.Raise();
        Assert.Single(agg.DomainEvents);
        Assert.Single(agg.DistributedEvents);

        agg.ClearDistributedEvents();
        Assert.Single(agg.DomainEvents);      // domain events untouched
        Assert.Empty(agg.DistributedEvents);

        agg.ClearDomainEvents();
        Assert.Empty(agg.DomainEvents);
    }
}

public class EnsureTests
{
    [Fact]
    public void NotNull_throws_on_null()
        => Assert.Throws<AppValidationException>(() => Ensure.NotNull<string>(null, "x"));

    [Fact]
    public void NotNullOrWhiteSpace_enforces_max_length()
        => Assert.Throws<AppValidationException>(() => Ensure.NotNullOrWhiteSpace("toolong", "x", maxLength: 3));

    [Fact]
    public void True_throws_business_rule()
        => Assert.Throws<BusinessRuleException>(() => Ensure.True(false, "nope"));

    [Fact]
    public void Positive_and_Range_validate()
    {
        Assert.Throws<AppValidationException>(() => Ensure.Positive(0, "n"));
        Assert.Throws<AppValidationException>(() => Ensure.Range(5, 1, 3, "n"));
        Assert.Equal(2, Ensure.Positive(2, "n"));
    }
}

public class SpecificationTests
{
    [Fact]
    public void And_Or_Not_compose()
    {
        var isTrue = new IsActive(true);
        var isFalse = new IsActive(false);

        Assert.True(isTrue.IsSatisfiedBy(true));
        Assert.False(isTrue.And(isFalse).IsSatisfiedBy(true));
        Assert.True(isTrue.Or(isFalse).IsSatisfiedBy(true));
        Assert.True(isTrue.Not().IsSatisfiedBy(false));
    }
}
