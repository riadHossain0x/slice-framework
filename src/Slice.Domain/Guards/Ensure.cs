using Slice.Domain.Exceptions;

namespace Slice.Domain.Guards;

/// <summary>Guard clauses for enforcing invariants inside domain methods.</summary>
public static class Ensure
{
    public static T NotNull<T>(T? value, string parameterName) where T : class
        => value ?? throw new AppValidationException($"'{parameterName}' must not be null.");

    public static string NotNullOrWhiteSpace(string? value, string parameterName, int? maxLength = null)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new AppValidationException($"'{parameterName}' must not be null or empty.");
        if (maxLength is { } max && value.Length > max)
            throw new AppValidationException($"'{parameterName}' must be at most {max} characters.");
        return value;
    }

    public static void True(bool condition, string message, string? code = null)
    {
        if (!condition)
            throw code is null ? new BusinessRuleException(message) : new BusinessRuleException(message, code);
    }

    public static int Positive(int value, string parameterName)
        => value > 0 ? value : throw new AppValidationException($"'{parameterName}' must be positive.");

    public static void Range<T>(T value, T min, T max, string parameterName) where T : IComparable<T>
    {
        if (value.CompareTo(min) < 0 || value.CompareTo(max) > 0)
            throw new AppValidationException($"'{parameterName}' must be between {min} and {max}.");
    }
}
