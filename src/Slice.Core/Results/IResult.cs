namespace Slice.Core.Results;

/// <summary>
/// Non-generic view over a <see cref="Result"/>/<see cref="Result{T}"/> so infrastructure
/// (e.g. the web layer) can map any result to a response without knowing the value type.
/// </summary>
public interface IResult
{
    bool IsSuccess { get; }
    Error? Error { get; }
    object? GetValue();
}
