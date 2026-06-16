using Slice.Core.DependencyInjection;

namespace Slice.Settings;

/// <summary>Resolves effective setting values across the provider chain and sets global overrides.</summary>
public interface ISettingManager
{
    Task<string?> GetOrNullAsync(string name);
    Task<T?> GetAsync<T>(string name, T? defaultValue = default) where T : IParsable<T>;
    Task SetGlobalAsync(string name, string? value);
    Task<IReadOnlyDictionary<string, string?>> GetAllAsync();
}

public sealed class SettingManager(
    ISettingDefinitionManager definitions,
    IEnumerable<ISettingValueProvider> valueProviders,
    IGlobalSettingStore globalStore)
    : ISettingManager, ITransientDependency
{
    private readonly ISettingValueProvider[] _providers = valueProviders.OrderBy(p => p.Order).ToArray();

    public async Task<string?> GetOrNullAsync(string name)
    {
        var definition = definitions.GetOrNull(name)
            ?? throw new InvalidOperationException($"Undefined setting: '{name}'.");

        foreach (var provider in _providers)
        {
            var value = await provider.GetOrNullAsync(definition);
            if (value is not null)
                return value;
        }
        return null;
    }

    public async Task<T?> GetAsync<T>(string name, T? defaultValue = default) where T : IParsable<T>
    {
        var raw = await GetOrNullAsync(name);
        return raw is not null && T.TryParse(raw, null, out var parsed) ? parsed : defaultValue;
    }

    public Task SetGlobalAsync(string name, string? value)
    {
        _ = definitions.GetOrNull(name) ?? throw new InvalidOperationException($"Undefined setting: '{name}'.");
        globalStore.Set(name, value);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyDictionary<string, string?>> GetAllAsync()
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var definition in definitions.GetAll())
            result[definition.Name] = await GetOrNullAsync(definition.Name);
        return result;
    }
}
