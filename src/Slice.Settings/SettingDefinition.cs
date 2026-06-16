using Slice.Core.DependencyInjection;

namespace Slice.Settings;

/// <summary>Declares a setting: its name, default value, and metadata.</summary>
public sealed class SettingDefinition(string name, string? defaultValue = null, string? displayName = null, bool isVisibleToClients = false)
{
    public string Name { get; } = name;
    public string? DefaultValue { get; } = defaultValue;
    public string DisplayName { get; } = displayName ?? name;
    public bool IsVisibleToClients { get; } = isVisibleToClients;
}

public interface ISettingDefinitionContext
{
    void Add(SettingDefinition definition);
}

/// <summary>Implement to declare a module's settings. Discovered + aggregated at startup.</summary>
public abstract class SettingDefinitionProvider : ITransientDependency
{
    public abstract void Define(ISettingDefinitionContext context);
}
