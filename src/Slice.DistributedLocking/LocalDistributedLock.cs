using System.Collections.Concurrent;
using Slice.Application;
using Slice.Core.Ambient;
using Slice.Core.DependencyInjection;
using Slice.Modularity;

namespace Slice.DistributedLocking;

/// <summary>
/// In-process distributed lock (per-key <see cref="SemaphoreSlim"/>). Correct for a single node;
/// replace with the Redis provider for true cross-node coordination.
/// </summary>
public sealed class LocalDistributedLock : IDistributedLock, ISingletonDependency
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        var acquired = await gate.WaitAsync(timeout ?? TimeSpan.Zero, ct);
        return acquired ? new Releaser(gate) : null;
    }

    private sealed class Releaser(SemaphoreSlim gate) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() { gate.Release(); return default; }
    }
}

/// <summary>Replaces the Core null lock with the in-process implementation.</summary>
[DependsOn(typeof(SliceApplicationModule))]
public sealed class SliceDistributedLockingModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
        => context.Services.AddSliceConventions(typeof(SliceDistributedLockingModule).Assembly);
}
