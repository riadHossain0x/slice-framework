using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Slice.EventBus;

namespace Slice.EntityFrameworkCore.Outbox;

/// <summary>Records processed incoming message ids for at-least-once dedup.</summary>
public sealed class InboxMessage
{
    public string MessageId { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; }
}

/// <summary>EF-backed inbox: first writer of a message id wins; redeliveries are skipped.</summary>
public sealed class EfInboxStore<TContext>(TContext db) : IInboxStore
    where TContext : SliceDbContext
{
    public async Task<bool> TryMarkProcessedAsync(string messageId, CancellationToken ct = default)
    {
        if (await db.Set<InboxMessage>().AnyAsync(m => m.MessageId == messageId, ct))
            return false;

        db.Set<InboxMessage>().Add(new InboxMessage { MessageId = messageId, ProcessedAt = DateTime.UtcNow });
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            return false; // concurrent insert of the same id
        }
    }
}

public static class InboxRegistration
{
    /// <summary>Use the EF inbox (on <typeparamref name="TContext"/>) for distributed-event dedup.</summary>
    public static IServiceCollection AddSliceInbox<TContext>(this IServiceCollection services)
        where TContext : SliceDbContext
    {
        services.RemoveAll<IInboxStore>();
        services.AddScoped<IInboxStore, EfInboxStore<TContext>>();
        return services;
    }
}
