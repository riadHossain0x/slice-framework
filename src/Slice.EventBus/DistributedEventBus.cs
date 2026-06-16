using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Slice.Core.DependencyInjection;
using Slice.Domain.Events;

namespace Slice.EventBus;

/// <summary>Handles an integration (distributed) event. Invoked by the outbox processor.</summary>
public interface IDistributedEventHandler<in TEvent> where TEvent : IDistributedEvent
{
    Task HandleAsync(TEvent @event, CancellationToken ct);
}

/// <summary>Dispatches a distributed event to its handlers. (In-process transport; swappable.)</summary>
public interface IDistributedEventBus
{
    Task PublishAsync(IDistributedEvent @event, CancellationToken ct = default);
}

/// <summary>
/// Scoped in-process distributed bus. Resolves <see cref="IDistributedEventHandler{TEvent}"/>s for
/// the event's runtime type (cached dispatcher) from the current scope. A real deployment swaps
/// this for a broker-backed transport.
/// </summary>
public sealed class DistributedEventBus(IServiceProvider serviceProvider) : IDistributedEventBus, IScopedDependency
{
    private static readonly ConcurrentDictionary<Type, EventDispatcher> Dispatchers = new();

    public Task PublishAsync(IDistributedEvent @event, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        var dispatcher = Dispatchers.GetOrAdd(@event.GetType(),
            t => (EventDispatcher)Activator.CreateInstance(typeof(EventDispatcher<>).MakeGenericType(t))!);
        return dispatcher.DispatchAsync(@event, serviceProvider, ct);
    }

    private abstract class EventDispatcher
    {
        public abstract Task DispatchAsync(IDistributedEvent @event, IServiceProvider sp, CancellationToken ct);
    }

    private sealed class EventDispatcher<TEvent> : EventDispatcher where TEvent : IDistributedEvent
    {
        public override async Task DispatchAsync(IDistributedEvent @event, IServiceProvider sp, CancellationToken ct)
        {
            foreach (var handler in sp.GetServices<IDistributedEventHandler<TEvent>>())
                await handler.HandleAsync((TEvent)@event, ct);
        }
    }
}

public static class DistributedEventBusRegistration
{
    /// <summary>Scans an assembly for closed <see cref="IDistributedEventHandler{TEvent}"/> implementations.</summary>
    public static IServiceCollection AddDistributedEventHandlers(this IServiceCollection services, Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (type is not { IsClass: true, IsAbstract: false })
                continue;

            foreach (var handlerInterface in type.GetInterfaces()
                         .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDistributedEventHandler<>)))
            {
                services.AddTransient(handlerInterface, type);
            }
        }
        return services;
    }
}
