using Slice.Core.DependencyInjection;

namespace Slice.Authorization;

/// <summary>A single permission (e.g. <c>Crm.Leads.Create</c>), optionally nested under a parent.</summary>
public sealed class PermissionDefinition
{
    private readonly List<PermissionDefinition> _children = [];

    internal PermissionDefinition(string name, string? displayName, bool grantedByDefault)
    {
        Name = name;
        DisplayName = displayName ?? name;
        GrantedByDefault = grantedByDefault;
    }

    public string Name { get; }
    public string DisplayName { get; }
    public bool GrantedByDefault { get; }
    public IReadOnlyList<PermissionDefinition> Children => _children;

    public PermissionDefinition AddChild(string name, string? displayName = null, bool grantedByDefault = false)
    {
        var child = new PermissionDefinition(name, displayName, grantedByDefault);
        _children.Add(child);
        return child;
    }
}

/// <summary>A named group of related permissions (typically one per module/feature area).</summary>
public sealed class PermissionGroupDefinition
{
    private readonly List<PermissionDefinition> _permissions = [];

    internal PermissionGroupDefinition(string name, string? displayName)
    {
        Name = name;
        DisplayName = displayName ?? name;
    }

    public string Name { get; }
    public string DisplayName { get; }
    public IReadOnlyList<PermissionDefinition> Permissions => _permissions;

    public PermissionDefinition AddPermission(string name, string? displayName = null, bool grantedByDefault = false)
    {
        var permission = new PermissionDefinition(name, displayName, grantedByDefault);
        _permissions.Add(permission);
        return permission;
    }
}

public interface IPermissionDefinitionContext
{
    PermissionGroupDefinition AddGroup(string name, string? displayName = null);
}

/// <summary>Implement to declare a module's permissions. Discovered + aggregated at startup.</summary>
public abstract class PermissionDefinitionProvider : ITransientDependency
{
    public abstract void Define(IPermissionDefinitionContext context);
}
