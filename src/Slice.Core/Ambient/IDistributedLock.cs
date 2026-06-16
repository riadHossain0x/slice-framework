using Slice.Core.DependencyInjection;

namespace Slice.Core.Ambient;

/// <summary>
/// A best-effort distributed mutex. <see cref="TryAcquireAsync"/> returns a disposable handle on
/// success or <c>null</c> if the lock is held elsewhere. The default is a no-op (single-node);
/// register a real provider (e.g. Redis) for multi-node coordination.
/// </summary>
public interface IDistributedLock
{
    Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan? timeout = null, CancellationToken ct = default);
}

/// <summary>Default lock that always "acquires" (single-node / no coordination).</summary>
public sealed class NullDistributedLock : IDistributedLock, ISingletonDependency
{
    public Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan? timeout = null, CancellationToken ct = default)
        => Task.FromResult<IAsyncDisposable?>(NoopHandle.Instance);

    private sealed class NoopHandle : IAsyncDisposable
    {
        public static readonly NoopHandle Instance = new();
        public ValueTask DisposeAsync() => default;
    }
}
