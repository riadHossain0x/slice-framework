using System.Linq.Expressions;

namespace Slice.Domain.Specifications;

/// <summary>An encapsulated, composable query predicate over <typeparamref name="T"/>.</summary>
public interface ISpecification<T>
{
    Expression<Func<T, bool>> ToExpression();
    bool IsSatisfiedBy(T entity);
}

public abstract class Specification<T> : ISpecification<T>
{
    public abstract Expression<Func<T, bool>> ToExpression();

    public bool IsSatisfiedBy(T entity) => ToExpression().Compile()(entity);

    public Specification<T> And(Specification<T> other) => new AndSpecification<T>(this, other);
    public Specification<T> Or(Specification<T> other) => new OrSpecification<T>(this, other);
    public Specification<T> Not() => new NotSpecification<T>(this);

    public static implicit operator Expression<Func<T, bool>>(Specification<T> spec) => spec.ToExpression();
}

internal sealed class AndSpecification<T>(Specification<T> left, Specification<T> right) : Specification<T>
{
    public override Expression<Func<T, bool>> ToExpression()
        => Combine(left.ToExpression(), right.ToExpression(), Expression.AndAlso);
    internal static Expression<Func<T, bool>> Combine(
        Expression<Func<T, bool>> l, Expression<Func<T, bool>> r,
        Func<Expression, Expression, BinaryExpression> merge)
    {
        var p = Expression.Parameter(typeof(T));
        var body = merge(
            new ReplaceParameterVisitor(l.Parameters[0], p).Visit(l.Body)!,
            new ReplaceParameterVisitor(r.Parameters[0], p).Visit(r.Body)!);
        return Expression.Lambda<Func<T, bool>>(body, p);
    }
}

internal sealed class OrSpecification<T>(Specification<T> left, Specification<T> right) : Specification<T>
{
    public override Expression<Func<T, bool>> ToExpression()
        => AndSpecification<T>.Combine(left.ToExpression(), right.ToExpression(), Expression.OrElse);
}

internal sealed class NotSpecification<T>(Specification<T> inner) : Specification<T>
{
    public override Expression<Func<T, bool>> ToExpression()
    {
        var expr = inner.ToExpression();
        return Expression.Lambda<Func<T, bool>>(Expression.Not(expr.Body), expr.Parameters);
    }
}

internal sealed class ReplaceParameterVisitor(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
{
    protected override Expression VisitParameter(ParameterExpression node)
        => node == from ? to : base.VisitParameter(node);
}
