using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Slice.Modularity;

/// <summary>Context passed to module service-configuration phases.</summary>
public sealed class ServiceConfigurationContext(IServiceCollection services, IConfiguration configuration)
{
    public IServiceCollection Services { get; } = services;
    public IConfiguration Configuration { get; } = configuration;

    /// <summary>Cross-module handshake bag (options producers/consumers between modules).</summary>
    public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>();
}

/// <summary>Context passed to module initialization (after the container is built).</summary>
public sealed class ApplicationInitializationContext(IServiceProvider serviceProvider)
{
    public IServiceProvider ServiceProvider { get; } = serviceProvider;
}

/// <summary>
/// Base class for a framework/bounded-context module. Declare dependencies with
/// <see cref="DependsOnAttribute"/>; modules are configured in topological order.
/// </summary>
public abstract class SliceModule
{
    public virtual void PreConfigureServices(ServiceConfigurationContext context) { }
    public virtual void ConfigureServices(ServiceConfigurationContext context) { }
    public virtual void PostConfigureServices(ServiceConfigurationContext context) { }
    public virtual Task OnApplicationInitializationAsync(ApplicationInitializationContext context) => Task.CompletedTask;
    public virtual Task OnApplicationShutdownAsync(ApplicationInitializationContext context) => Task.CompletedTask;
}
