using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Slice.Mediator.Default;

public static class MediatorRegistration
{
    /// <summary>Registers the default <see cref="ISender"/> engine.</summary>
    public static IServiceCollection AddSliceMediator(this IServiceCollection services)
    {
        services.TryAddScoped<ISender, DefaultSender>();
        return services;
    }

    /// <summary>
    /// Scans an assembly for closed <see cref="IRequestHandler{TRequest,TResponse}"/> implementations
    /// and registers each against its handler interface (transient).
    /// </summary>
    public static IServiceCollection AddRequestHandlers(this IServiceCollection services, Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (type is not { IsClass: true, IsAbstract: false })
                continue;

            foreach (var handlerInterface in type.GetInterfaces()
                         .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)))
            {
                services.AddTransient(handlerInterface, type);
            }
        }

        return services;
    }
}
