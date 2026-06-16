namespace Slice.EntityFrameworkCore.Outbox;

/// <summary>
/// A persisted integration event awaiting delivery. Written in the same transaction as the
/// domain changes that raised it (transactional outbox), then delivered by the outbox processor.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }

    /// <summary>Assembly-qualified type name of the <c>IDistributedEvent</c>.</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>JSON-serialized event payload.</summary>
    public string Payload { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public int RetryCount { get; set; }
    public string? Error { get; set; }
}
