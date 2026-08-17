using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Slice.Core.DependencyInjection;
using Slice.Domain.Events;

namespace Slice.EventBus;

/// <summary>
/// Default in-process event bus. Resolves <see cref="IDomainEventHandler{TEvent}"/>s for an
/// event's runtime type (cached dispatcher per type) and invokes them sequentially.
///
/// Handlers are resolved from a scope this bus creates per publish, <em>not</em> from its own injected
/// provider. That distinction is load-bearing: the bus is a singleton (<c>DomainEventInterceptor</c>
/// is one too and injects it), so its own provider is the ROOT container, and handlers — registered
/// transient by <c>AddDomainEventHandlers</c> — almost always depend on a scoped <c>DbContext</c>.
/// Resolving those from the root provider throws under scope validation, and silently produces
/// root-captive, process-lifetime instances without it.
///
/// Two properties make the fresh scope safe rather than merely legal. Nothing here runs inside an
/// ambient transaction — <c>UnitOfWorkBehavior</c> just calls <c>SaveChangesAsync</c> per unit of work,
/// so by the time the interceptor dispatches on <c>SavedChanges</c> the rows are committed and a new
/// scope's <c>DbContext</c> sees them. And the ambient state handlers care about — <c>ICurrentTenant</c>,
/// <c>IDataFilter</c> — is AsyncLocal-backed rather than scope-backed, so the current tenant and any
/// <c>Disable&lt;T&gt;()</c> still apply inside the new scope.
/// </summary>
public sealed class LocalEventBus(IServiceScopeFactory scopeFactory) : ILocalEventBus, ISingletonDependency
{
    private static readonly ConcurrentDictionary<Type, EventDispatcher> Dispatchers = new();

    public async Task PublishAsync(IDomainEvent @event, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        using var scope = scopeFactory.CreateScope();
        await DispatchAsync(@event, scope.ServiceProvider, ct);
    }

    public async Task PublishAsync(IEnumerable<IDomainEvent> events, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(events);

        // One scope for the whole batch: events raised by the same save are handled together, so their
        // handlers should see the same scoped services — and a batch costs one scope, not one per event.
        using var scope = scopeFactory.CreateScope();
        foreach (var @event in events)
        {
            ArgumentNullException.ThrowIfNull(@event);
            await DispatchAsync(@event, scope.ServiceProvider, ct);
        }
    }

    private static Task DispatchAsync(IDomainEvent @event, IServiceProvider sp, CancellationToken ct)
    {
        var dispatcher = Dispatchers.GetOrAdd(@event.GetType(),
            t => (EventDispatcher)Activator.CreateInstance(typeof(EventDispatcher<>).MakeGenericType(t))!);
        return dispatcher.DispatchAsync(@event, sp, ct);
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
