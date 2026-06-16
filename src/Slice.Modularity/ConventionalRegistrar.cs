using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Slice.Core.DependencyInjection;

namespace Slice.Modularity;

/// <summary>
/// Scans an assembly and auto-registers every concrete class that implements a DI marker
/// (<see cref="ITransientDependency"/>/<see cref="IScopedDependency"/>/<see cref="ISingletonDependency"/>),
/// against itself and each of its directly-implemented non-marker interfaces.
/// </summary>
public static class ConventionalRegistrar
{
    private static readonly Type[] Markers =
        [typeof(ITransientDependency), typeof(IScopedDependency), typeof(ISingletonDependency)];

    public static IServiceCollection AddSliceConventions(this IServiceCollection services, Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (type is not { IsClass: true, IsAbstract: false })
                continue;

            var lifetime = ResolveLifetime(type);
            if (lifetime is null)
                continue;

            // self-registration
            services.TryAddEnumerableSelf(type, lifetime.Value);

            // expose implemented interfaces (excluding the markers themselves)
            foreach (var iface in type.GetInterfaces().Where(i => !Markers.Contains(i)))
                services.Add(new ServiceDescriptor(iface, sp => sp.GetRequiredService(type), lifetime.Value));

            // expose abstract base classes that themselves carry a DI marker (e.g. provider base
            // classes like PermissionDefinitionProvider), so IEnumerable<TBase> resolves all of them.
            for (var baseType = type.BaseType; baseType is not null && baseType != typeof(object); baseType = baseType.BaseType)
                if (baseType.IsAbstract && Markers.Any(m => m.IsAssignableFrom(baseType)))
                    services.Add(new ServiceDescriptor(baseType, sp => sp.GetRequiredService(type), lifetime.Value));
        }

        return services;
    }

    private static ServiceLifetime? ResolveLifetime(Type type)
    {
        if (typeof(ISingletonDependency).IsAssignableFrom(type)) return ServiceLifetime.Singleton;
        if (typeof(IScopedDependency).IsAssignableFrom(type)) return ServiceLifetime.Scoped;
        if (typeof(ITransientDependency).IsAssignableFrom(type)) return ServiceLifetime.Transient;
        return null;
    }

    private static void TryAddEnumerableSelf(this IServiceCollection services, Type type, ServiceLifetime lifetime)
    {
        if (services.Any(d => d.ServiceType == type && d.ImplementationType == type))
            return;
        services.Add(new ServiceDescriptor(type, type, lifetime));
    }
}
