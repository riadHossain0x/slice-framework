using Slice.Core.DependencyInjection;

namespace Slice.Settings;

public interface ISettingDefinitionManager
{
    SettingDefinition? GetOrNull(string name);
    IReadOnlyList<SettingDefinition> GetAll();
}

public sealed class SettingDefinitionManager(IEnumerable<SettingDefinitionProvider> providers)
    : ISettingDefinitionManager, ISingletonDependency
{
    private readonly Lazy<Dictionary<string, SettingDefinition>> _definitions = new(() =>
    {
        var context = new Context();
        foreach (var provider in providers)
            provider.Define(context);
        return context.Definitions;
    });

    public SettingDefinition? GetOrNull(string name) => _definitions.Value.GetValueOrDefault(name);
    public IReadOnlyList<SettingDefinition> GetAll() => _definitions.Value.Values.ToList();

    private sealed class Context : ISettingDefinitionContext
    {
        public Dictionary<string, SettingDefinition> Definitions { get; } = new(StringComparer.Ordinal);
        public void Add(SettingDefinition definition) => Definitions[definition.Name] = definition;
    }
}
