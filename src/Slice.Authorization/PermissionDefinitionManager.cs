using Slice.Core.DependencyInjection;

namespace Slice.Authorization;

/// <summary>Aggregated, flattened view of every declared permission.</summary>
public interface IPermissionDefinitionManager
{
    IReadOnlyList<PermissionGroupDefinition> GetGroups();
    PermissionDefinition? Find(string name);
    IReadOnlyList<PermissionDefinition> GetPermissions();
}

public sealed class PermissionDefinitionManager(IEnumerable<PermissionDefinitionProvider> providers)
    : IPermissionDefinitionManager, ISingletonDependency, IPermissionDefinitionContext
{
    private readonly Lazy<(List<PermissionGroupDefinition> Groups, Dictionary<string, PermissionDefinition> Flat)> _model =
        new(() => Build(providers));

    private readonly List<PermissionGroupDefinition> _building = [];

    public IReadOnlyList<PermissionGroupDefinition> GetGroups() => _model.Value.Groups;
    public PermissionDefinition? Find(string name) => _model.Value.Flat.GetValueOrDefault(name);
    public IReadOnlyList<PermissionDefinition> GetPermissions() => _model.Value.Flat.Values.ToList();

    PermissionGroupDefinition IPermissionDefinitionContext.AddGroup(string name, string? displayName)
    {
        var group = new PermissionGroupDefinition(name, displayName);
        _building.Add(group);
        return group;
    }

    private static (List<PermissionGroupDefinition>, Dictionary<string, PermissionDefinition>) Build(
        IEnumerable<PermissionDefinitionProvider> providers)
    {
        var context = new PermissionDefinitionManager([]);
        foreach (var provider in providers)
            provider.Define(context);

        var flat = new Dictionary<string, PermissionDefinition>(StringComparer.Ordinal);
        void Walk(PermissionDefinition p)
        {
            flat[p.Name] = p;
            foreach (var c in p.Children) Walk(c);
        }
        foreach (var group in context._building)
            foreach (var p in group.Permissions)
                Walk(p);

        return (context._building, flat);
    }
}
