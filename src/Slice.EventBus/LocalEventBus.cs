using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Slice.Core.DependencyInjection;
using Slice.Domain.Events;

namespace Slice.EventBus;

/// <summary>
/// Default in-process event bus. Resolves <see cref="IDomainEventHandler{TEvent}"/>s for an
/// event's runtime type (cached dispatcher per type) and invokes them sequentially.
/// </summary>
public sealed class LocalEventBus(IServiceProvider serviceProvider) : ILocalEventBus, ISingletonDependency
{
    private static readonly ConcurrentDictionary<Type, EventDispatcher> Dispatchers = new();

    public Task PublishAsync(IDomainEvent @event, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        var dispatcher = Dispatchers.GetOrAdd(@event.GetType(),
            t => (EventDispatcher)Activator.CreateInstance(typeof(EventDispatcher<>).MakeGenericType(t))!);
        return dispatcher.DispatchAsync(@event, serviceProvider, ct);
    }

    public async Task PublishAsync(IEnumerable<IDomainEvent> events, CancellationToken ct = default)
    {
        foreach (var @event in events)
            await PublishAsync(@event, ct);
    }

    private abstract class EventDispatcher
    {
        public abstract Task DispatchAsync(IDomainEvent @event, IServiceProvider sp, CancellationToken ct);
    }

    private sealed class EventDispatcher<TEvent> : EventDispatcher where TEvent : IDomainEvent
    {
        public override async Task DispatchAsync(IDomainEvent @event, IServiceProvider sp, CancellationToken ct)
        {
            foreach (var handler in sp.GetServices<IDomainEventHandler<TEvent>>())
                await handler.HandleAsync((TEvent)@event, ct);
        }
    }
}
