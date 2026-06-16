using Slice.Core.DependencyInjection;

namespace Slice.Core.Ambient;

/// <summary>Default no-tenant ambient. Replaced by the MultiTenancy module when present.</summary>
public sealed class NullCurrentTenant : ICurrentTenant, ISingletonDependency
{
    public bool IsAvailable => false;
    public Guid? Id => null;
    public string? Name => null;
    public IDisposable Change(Guid? tenantId, string? name = null) => NullDisposable.Instance;
}

/// <summary>Default anonymous user. Replaced by the auth module when present.</summary>
public sealed class NullCurrentUser : ICurrentUser, ISingletonDependency
{
    public bool IsAuthenticated => false;
    public Guid? Id => null;
    public string? UserName => null;
    public string[] Roles => [];
}

internal sealed class NullDisposable : IDisposable
{
    public static readonly NullDisposable Instance = new();
    public void Dispose() { }
}
