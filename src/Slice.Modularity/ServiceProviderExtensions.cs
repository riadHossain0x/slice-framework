using Microsoft.Extensions.DependencyInjection;

namespace Slice.Modularity;

public static class ServiceProviderExtensions
{
    /// <summary>Runs every module's <c>OnApplicationInitializationAsync</c> in dependency-first order.</summary>
    public static async Task InitializeSliceModulesAsync(this IServiceProvider serviceProvider)
    {
        var manager = serviceProvider.GetRequiredService<SliceModuleManager>();
        await manager.InitializeAsync(serviceProvider);
    }

    /// <summary>Runs every module's <c>OnApplicationShutdownAsync</c> in reverse order.</summary>
    public static async Task ShutdownSliceModulesAsync(this IServiceProvider serviceProvider)
    {
        var manager = serviceProvider.GetRequiredService<SliceModuleManager>();
        await manager.ShutdownAsync(serviceProvider);
    }
}
