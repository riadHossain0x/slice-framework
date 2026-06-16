using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Slice.Core.Ambient;
using Slice.Core.DependencyInjection;
using Slice.Domain.Events;
using Slice.EntityFrameworkCore.Outbox;
using Slice.EventBus;

namespace Slice.EntityFrameworkCore.Interceptors;

/// <summary>
/// Bridges aggregate events to the buses:
/// <list type="bullet">
/// <item>On <c>SavingChanges</c>: serializes each <see cref="IDistributedEvent"/> into an
/// <see cref="OutboxMessage"/> row in the <em>same</em> transaction (transactional outbox), then
/// clears the distributed events.</item>
/// <item>On <c>SavedChanges</c>: dispatches the local <see cref="IDomainEvent"/>s in-process and
/// clears them.</item>
/// </list>
/// </summary>
public sealed class DomainEventInterceptor(ILocalEventBus localEventBus, IClock clock)
    : SaveChangesInterceptor, ISingletonDependency
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        WriteOutbox(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        WriteOutbox(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken ct = default)
    {
        await DispatchLocalAsync(eventData.Context, ct);
        return await base.SavedChangesAsync(eventData, result, ct);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        DispatchLocalAsync(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();
        return base.SavedChanges(eventData, result);
    }

    private void WriteOutbox(DbContext? context)
    {
        if (context is null)
            return;

        foreach (var holder in Holders(context))
        {
            foreach (var @event in holder.DistributedEvents)
            {
                context.Add(new OutboxMessage
                {
                    Id = Guid.CreateVersion7(),
                    EventType = @event.GetType().AssemblyQualifiedName!,
                    Payload = JsonSerializer.Serialize(@event, @event.GetType()),
                    CreatedAt = clock.Now
                });
            }
            holder.ClearDistributedEvents();
        }
    }

    private async Task DispatchLocalAsync(DbContext? context, CancellationToken ct)
    {
        if (context is null)
            return;

        var holders = Holders(context);
        var events = holders.SelectMany(h => h.DomainEvents).ToList();
        foreach (var holder in holders)
            holder.ClearDomainEvents();

        foreach (var @event in events)
            await localEventBus.PublishAsync(@event, ct);
    }

    private static List<IHasDomainEvents> Holders(DbContext context)
        => context.ChangeTracker.Entries()
            .Select(e => e.Entity)
            .OfType<IHasDomainEvents>()
            .ToList();
}
