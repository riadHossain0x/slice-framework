using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Slice.EventBus;

public static class EventBusRegistration
{
    /// <summary>
    /// Scans an assembly for closed <see cref="IDomainEventHandler{TEvent}"/> implementations and
    /// registers each (transient) against its handler interface.
    /// </summary>
    public static IServiceCollection AddDomainEventHandlers(this IServiceCollection services, Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (type is not { IsClass: true, IsAbstract: false })
                continue;

            foreach (var handlerInterface in type.GetInterfaces()
                         .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>)))
            {
                services.AddTransient(handlerInterface, type);
            }
        }

        return services;
    }
}
