namespace Slice.Domain.Exceptions;

/// <summary>Base type for expected domain failures. Carries a stable <see cref="Code"/>.</summary>
public abstract class DomainException : Exception
{
    protected DomainException(string code, string message, Exception? inner = null)
        : base(message, inner) => Code = code;

    public string Code { get; }
}

/// <summary>A business invariant was violated. Maps to HTTP 409.</summary>
public sealed class BusinessRuleException(string message, string code = "Domain:BusinessRule")
    : DomainException(code, message);

/// <summary>A required entity was not found. Maps to HTTP 404.</summary>
public sealed class EntityNotFoundException(string message, string code = "Domain:NotFound")
    : DomainException(code, message)
{
    public static EntityNotFoundException For<TEntity>(object key)
        => new($"{typeof(TEntity).Name} '{key}' was not found.");
}

/// <summary>Input failed a domain validation rule. Maps to HTTP 400.</summary>
public sealed class AppValidationException(string message, string code = "Domain:Validation")
    : DomainException(code, message);
