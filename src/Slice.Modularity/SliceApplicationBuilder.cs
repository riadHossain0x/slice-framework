using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Slice.Modularity;

/// <summary>Holds the ordered module set; resolved from DI to run init/shutdown.</summary>
public sealed class SliceModuleManager(IReadOnlyList<ModuleDescriptor> modules)
{
    public IReadOnlyList<ModuleDescriptor> Modules { get; } = modules;

    public async Task InitializeAsync(IServiceProvider serviceProvider)
    {
        var context = new ApplicationInitializationContext(serviceProvider);
        foreach (var module in Modules) // dependency-first
            await module.Instance.OnApplicationInitializationAsync(context);
    }

    public async Task ShutdownAsync(IServiceProvider serviceProvider)
    {
        var context = new ApplicationInitializationContext(serviceProvider);
        foreach (var module in Modules.Reverse()) // dependents first
            await module.Instance.OnApplicationShutdownAsync(context);
    }
}

public static class SliceApplicationBuilder
{
    /// <summary>
    /// Loads the module graph rooted at <typeparamref name="TRootModule"/>, runs the
    /// Pre/Configure/Post service-configuration phases in topological order, and registers
    /// a <see cref="SliceModuleManager"/> for later initialization.
    /// </summary>
    public static IServiceCollection AddSliceModules<TRootModule>(
        this IServiceCollection services, IConfiguration configuration)
        where TRootModule : SliceModule
    {
        var modules = ModuleLoader.LoadModules(typeof(TRootModule));
        var context = new ServiceConfigurationContext(services, configuration);

        foreach (var module in modules) module.Instance.PreConfigureServices(context);
        foreach (var module in modules) module.Instance.ConfigureServices(context);
        foreach (var module in modules) module.Instance.PostConfigureServices(context);

        services.AddSingleton(new SliceModuleManager(modules));
        return services;
    }
}
