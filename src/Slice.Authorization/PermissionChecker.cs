using Microsoft.Extensions.Configuration;
using Slice.Core.DependencyInjection;

namespace Slice.Authorization;

/// <summary>Source of permission grants for the current caller. Pluggable (config, EF, roles…).</summary>
public interface IPermissionStore
{
    Task<bool> IsGrantedAsync(string permission, CancellationToken ct = default);
}

/// <summary>Checks whether the current caller is granted a permission.</summary>
public interface IPermissionChecker
{
    Task<bool> IsGrantedAsync(string permission, CancellationToken ct = default);
}

public sealed class PermissionChecker(IPermissionStore store) : IPermissionChecker, ITransientDependency
{
    public Task<bool> IsGrantedAsync(string permission, CancellationToken ct = default)
        => store.IsGrantedAsync(permission, ct);
}

/// <summary>
/// Default store: grants the permissions listed under <c>Authorization:GrantedPermissions</c> in
/// configuration (plus any declared <c>grantedByDefault</c>). Real apps replace this with a
/// role/user/tenant-backed store (e.g. EF).
/// </summary>
public sealed class ConfigurationPermissionStore : IPermissionStore, ISingletonDependency
{
    private readonly HashSet<string> _granted;

    public ConfigurationPermissionStore(IConfiguration configuration, IPermissionDefinitionManager definitions)
    {
        _granted = configuration.GetSection("Authorization:GrantedPermissions")
            .GetChildren()
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var permission in definitions.GetPermissions())
            if (permission.GrantedByDefault)
                _granted.Add(permission.Name);
    }

    public Task<bool> IsGrantedAsync(string permission, CancellationToken ct = default)
        => Task.FromResult(_granted.Contains(permission));
}
