using System.Collections.Concurrent;
using System.Reflection;
using Slice.Core.Results;

namespace Slice.Application.Results;

/// <summary>Thrown when a pipeline failure can't be expressed as a <see cref="Result"/> response.</summary>
public sealed class SlicePipelineException(Error error) : Exception(error.Message)
{
    public Error Error { get; } = error;
}

/// <summary>
/// Produces a failed response of an arbitrary pipeline response type. When the response is
/// <see cref="Result"/> or <see cref="Result{T}"/>, returns a typed failure so behaviors can
/// short-circuit without throwing; otherwise throws <see cref="SlicePipelineException"/>.
/// </summary>
public static class ResultFactory
{
    private static readonly ConcurrentDictionary<Type, Func<Error, object>?> Factories = new();

    public static TResponse FailureOrThrow<TResponse>(Error error)
    {
        var factory = Factories.GetOrAdd(typeof(TResponse), BuildFactory);
        if (factory is null)
            throw new SlicePipelineException(error);
        return (TResponse)factory(error);
    }

    private static Func<Error, object>? BuildFactory(Type responseType)
    {
        if (responseType == typeof(Result))
            return error => Result.Failure(error);

        if (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var method = responseType.GetMethod(nameof(Result<object>.Failure), BindingFlags.Public | BindingFlags.Static)!;
            return error => method.Invoke(null, [error])!;
        }

        return null;
    }
}
