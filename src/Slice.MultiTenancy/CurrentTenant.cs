using Slice.Core.Ambient;
using Slice.Core.DependencyInjection;

namespace Slice.MultiTenancy;

/// <summary>Immutable ambient tenant snapshot.</summary>
public sealed record TenantInfo(Guid? Id, string? Name);

/// <summary>
/// AsyncLocal-backed <see cref="ICurrentTenant"/>. <see cref="Change"/> pushes a tenant for the
/// current async flow and returns an <see cref="IDisposable"/> that restores the previous value.
/// Registered after the Core null default, so it wins resolution.
/// </summary>
public sealed class CurrentTenant : ICurrentTenant, ISingletonDependency
{
    private static readonly AsyncLocal<TenantInfo?> Current = new();

    public bool IsAvailable => Current.Value?.Id is not null;
    public Guid? Id => Current.Value?.Id;
    public string? Name => Current.Value?.Name;

    public IDisposable Change(Guid? tenantId, string? name = null)
    {
        var previous = Current.Value;
        Current.Value = new TenantInfo(tenantId, name);
        return new Restore(() => Current.Value = previous);
    }

    private sealed class Restore(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
