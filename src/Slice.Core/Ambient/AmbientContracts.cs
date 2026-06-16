namespace Slice.Core.Ambient;

/// <summary>Ambient access to the current tenant. Push a scope with <see cref="Change"/>.</summary>
public interface ICurrentTenant
{
    bool IsAvailable { get; }
    Guid? Id { get; }
    string? Name { get; }

    /// <summary>Pushes a tenant onto the ambient scope; dispose to restore the previous value.</summary>
    IDisposable Change(Guid? tenantId, string? name = null);
}

/// <summary>Ambient access to the authenticated user (if any).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? Id { get; }
    string? UserName { get; }
    string[] Roles { get; }
}

/// <summary>Abstracts the system clock for testability. Always returns UTC.</summary>
public interface IClock
{
    DateTime Now { get; }
}

/// <summary>Generates stable identifiers (sequential GUIDs for index-friendliness).</summary>
public interface IGuidGenerator
{
    Guid Create();
}
