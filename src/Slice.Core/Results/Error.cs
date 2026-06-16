namespace Slice.Core.Results;

/// <summary>Classifies an <see cref="Error"/> so the web layer can map it to an HTTP status.</summary>
public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    Unauthorized,
    Unexpected
}

/// <summary>
/// An expected, non-exceptional failure carried by a <see cref="Result"/>/<see cref="Result{T}"/>.
/// Business outcomes travel as errors; programming/infra faults throw.
/// </summary>
public sealed record Error(
    string Code,
    string Message,
    ErrorType Type,
    IReadOnlyDictionary<string, string[]>? Details = null)
{
    public static Error Validation(string code, string message, IReadOnlyDictionary<string, string[]>? details = null)
        => new(code, message, ErrorType.Validation, details);

    public static Error NotFound(string code, string message)
        => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message)
        => new(code, message, ErrorType.Conflict);

    public static Error Forbidden(string code, string message)
        => new(code, message, ErrorType.Forbidden);

    public static Error Unauthorized(string code, string message)
        => new(code, message, ErrorType.Unauthorized);

    public static Error Unexpected(string code, string message)
        => new(code, message, ErrorType.Unexpected);
}
