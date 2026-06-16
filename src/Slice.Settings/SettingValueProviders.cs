using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Slice.Core.DependencyInjection;

namespace Slice.Settings;

/// <summary>A source of setting values. Providers are consulted highest-priority first.</summary>
public interface ISettingValueProvider
{
    /// <summary>Lower runs first (higher priority).</summary>
    int Order { get; }
    Task<string?> GetOrNullAsync(SettingDefinition setting);
}

/// <summary>In-memory global override store, settable at runtime via <c>ISettingManager.SetGlobalAsync</c>.</summary>
public interface IGlobalSettingStore
{
    string? GetOrNull(string name);
    void Set(string name, string? value);
}

public sealed class InMemoryGlobalSettingStore : IGlobalSettingStore, ISingletonDependency
{
    private readonly ConcurrentDictionary<string, string?> _values = new(StringComparer.Ordinal);
    public string? GetOrNull(string name) => _values.GetValueOrDefault(name);
    public void Set(string name, string? value) => _values[name] = value;
}

/// <summary>Highest priority: runtime global overrides.</summary>
public sealed class GlobalSettingValueProvider(IGlobalSettingStore store) : ISettingValueProvider, ITransientDependency
{
    public int Order => 0;
    public Task<string?> GetOrNullAsync(SettingDefinition setting) => Task.FromResult(store.GetOrNull(setting.Name));
}

/// <summary>Reads <c>Settings:{name}</c> from configuration (appsettings/env).</summary>
public sealed class ConfigurationSettingValueProvider(IConfiguration configuration) : ISettingValueProvider, ITransientDependency
{
    public int Order => 10;
    public Task<string?> GetOrNullAsync(SettingDefinition setting)
        => Task.FromResult(configuration[$"Settings:{setting.Name}"]);
}

/// <summary>Lowest priority: the declared default value.</summary>
public sealed class DefaultValueSettingValueProvider : ISettingValueProvider, ITransientDependency
{
    public int Order => 100;
    public Task<string?> GetOrNullAsync(SettingDefinition setting) => Task.FromResult(setting.DefaultValue);
}
