using Slice.Domain.Events;

namespace Slice.Sample.Crm.Domain.Leads.Events;

/// <summary>Raised when a lead is created. Will flow through the outbox once P9 lands.</summary>
public sealed record LeadCreatedEto(Guid LeadId, string? Email) : IDistributedEvent;

/// <summary>Local (in-process) counterpart — dispatched by the domain-event interceptor after save.</summary>
public sealed record LeadCreatedDomainEvent(Guid LeadId, string FullName) : IDomainEvent;
