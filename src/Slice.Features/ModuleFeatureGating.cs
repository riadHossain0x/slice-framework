using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Slice.Core.DependencyInjection;

namespace Slice.Features;

/// <summary>A registered "every request in this assembly requires this feature" rule (module-level gating).</summary>
public sealed record ModuleFeatureRequirement(Assembly Assembly, string Feature);

/// <summary>Looks up the module-level feature requirements that apply to a request's assembly.</summary>
public interface IModuleFeatureRegistry
{
    IReadOnlyList<string> For(Assembly assembly);
}

public sealed class ModuleFeatureRegistry(IEnumerable<ModuleFeatureRequirement> requirements)
    : IModuleFeatureRegistry, ISingletonDependency
{
    private static readonly string[] None = [];
    private readonly ILookup<Assembly, string> _map = requirements.ToLookup(r => r.Assembly, r => r.Feature);

    public IReadOnlyList<string> For(Assembly assembly)
        => _map.Contains(assembly) ? _map[assembly].Distinct().ToArray() : None;
}

public static class ModuleFeatureGating
{
    /// <summary>
    /// Gate <b>every</b> request defined in <typeparamref name="TModule"/>'s assembly behind a feature —
    /// one line in the module instead of <c>[RequiresFeature]</c> on each slice. Composes with per-request
    /// attributes (a request requires the union). Declare the feature in a <see cref="FeatureDefinitionProvider"/>.
    /// </summary>
    public static IServiceCollection RequireFeature<TModule>(this IServiceCollection services, string feature)
        => services.RequireFeatureForAssembly(typeof(TModule).Assembly, feature);

    /// <summary>Gate every request defined in <paramref name="assembly"/> behind <paramref name="feature"/>.</summary>
    public static IServiceCollection RequireFeatureForAssembly(this IServiceCollection services, Assembly assembly, string feature)
    {
        services.AddSingleton(new ModuleFeatureRequirement(assembly, feature));
        return services;
    }
}
