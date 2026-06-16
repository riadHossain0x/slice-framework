using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Slice.Core.Ambient;
using Slice.Domain.Events;
using Slice.EventBus;

namespace Slice.EntityFrameworkCore.Outbox;

/// <summary>
/// Polls the outbox of <typeparamref name="TContext"/> and delivers pending messages through the
/// distributed event bus (at-least-once; failures are retried up to <see cref="MaxRetries"/>).
/// One processor is registered per Slice DbContext.
/// </summary>
public sealed class OutboxProcessor<TContext>(IServiceScopeFactory scopeFactory, ILogger<OutboxProcessor<TContext>> logger)
    : BackgroundService
    where TContext : SliceDbContext
{
    private const int MaxRetries = 5;
    private const int BatchSize = 50;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox processing failed for {Context}", typeof(TContext).Name);
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();

        // Only one node drains a given context's outbox at a time (no-op lock by default).
        var distributedLock = scope.ServiceProvider.GetRequiredService<IDistributedLock>();
        await using var handle = await distributedLock.TryAcquireAsync($"outbox:{typeof(TContext).Name}", ct: ct);
        if (handle is null)
            return;

        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IDistributedEventPublisher>();

        var pending = await db.OutboxMessages
            .Where(m => m.ProcessedAt == null && m.RetryCount < MaxRetries)
            .OrderBy(m => m.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (pending.Count == 0)
            return;

        foreach (var message in pending)
        {
            try
            {
                var type = Type.GetType(message.EventType)
                    ?? throw new InvalidOperationException($"Unknown event type '{message.EventType}'.");
                var @event = (IDistributedEvent)JsonSerializer.Deserialize(message.Payload, type)!;
                await publisher.PublishAsync(@event, message.Id.ToString(), ct);
                message.ProcessedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                message.RetryCount++;
                message.Error = ex.Message;
                logger.LogWarning(ex, "Outbox message {Id} failed (attempt {Retry})", message.Id, message.RetryCount);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
