using Slice.Core.DependencyInjection;

namespace Slice.Core.Ambient;

/// <summary>
/// Ambient toggle for global query filters (soft-delete, multi-tenant). Filters are enabled by
/// default; wrap a block in <c>using (dataFilter.Disable&lt;ISoftDelete&gt;())</c> to bypass one
/// (e.g. host cross-tenant access or restoring a soft-deleted row).
/// </summary>
public interface IDataFilter
{
    bool IsEnabled<TFilter>();
    IDisposable Disable<TFilter>();
    IDisposable Enable<TFilter>();
}

/// <summary>Default ambient (AsyncLocal) data-filter state. Filters default to enabled.</summary>
public sealed class DataFilter : IDataFilter, ISingletonDependency
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Type, AsyncLocal<bool?>> _state = new();

    public bool IsEnabled<TFilter>() => Slot<TFilter>().Value ?? true;

    public IDisposable Disable<TFilter>() => Set<TFilter>(false);
    public IDisposable Enable<TFilter>() => Set<TFilter>(true);

    private AsyncLocal<bool?> Slot<TFilter>() => _state.GetOrAdd(typeof(TFilter), _ => new AsyncLocal<bool?>());

    private IDisposable Set<TFilter>(bool enabled)
    {
        var slot = Slot<TFilter>();
        var previous = slot.Value;
        slot.Value = enabled;
        return new Restore(() => slot.Value = previous);
    }

    private sealed class Restore(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
