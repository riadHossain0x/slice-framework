using Microsoft.Extensions.Configuration;
using Slice.Core.DependencyInjection;

namespace Slice.Features;

public sealed class FeatureDefinition(string name, string? defaultValue = "false", string? displayName = null)
{
    public string Name { get; } = name;
    public string? DefaultValue { get; } = defaultValue;
    public string DisplayName { get; } = displayName ?? name;
}

public interface IFeatureDefinitionContext { void Add(FeatureDefinition definition); }

/// <summary>Implement to declare a module's features. Discovered + aggregated at startup.</summary>
public abstract class FeatureDefinitionProvider : ITransientDependency
{
    public abstract void Define(IFeatureDefinitionContext context);
}

public interface IFeatureDefinitionManager
{
    FeatureDefinition? GetOrNull(string name);
    IReadOnlyList<FeatureDefinition> GetAll();
}

public sealed class FeatureDefinitionManager(IEnumerable<FeatureDefinitionProvider> providers)
    : IFeatureDefinitionManager, ISingletonDependency
{
    private readonly Lazy<Dictionary<string, FeatureDefinition>> _defs = new(() =>
    {
        var ctx = new Ctx();
        foreach (var p in providers) p.Define(ctx);
        return ctx.Items;
    });

    public FeatureDefinition? GetOrNull(string name) => _defs.Value.GetValueOrDefault(name);
    public IReadOnlyList<FeatureDefinition> GetAll() => _defs.Value.Values.ToList();

    private sealed class Ctx : IFeatureDefinitionContext
    {
        public Dictionary<string, FeatureDefinition> Items { get; } = new(StringComparer.Ordinal);
        public void Add(FeatureDefinition d) => Items[d.Name] = d;
    }
}

/// <summary>Source of feature values (config default). Real apps add tenant/edition stores.</summary>
public interface IFeatureStore { Task<string?> GetOrNullAsync(string name); }

public sealed class ConfigurationFeatureStore(IConfiguration configuration) : IFeatureStore, ISingletonDependency
{
    public Task<string?> GetOrNullAsync(string name) => Task.FromResult(configuration[$"Features:{name}"]);
}

public interface IFeatureChecker
{
    Task<string?> GetOrNullAsync(string name);
    Task<bool> IsEnabledAsync(string name);
}

public sealed class FeatureChecker(IFeatureDefinitionManager definitions, IFeatureStore store)
    : IFeatureChecker, ITransientDependency
{
    public async Task<string?> GetOrNullAsync(string name)
    {
        var def = definitions.GetOrNull(name) ?? throw new InvalidOperationException($"Undefined feature: '{name}'.");
        return await store.GetOrNullAsync(name) ?? def.DefaultValue;
    }

    public async Task<bool> IsEnabledAsync(string name)
        => string.Equals(await GetOrNullAsync(name), "true", StringComparison.OrdinalIgnoreCase);
}
