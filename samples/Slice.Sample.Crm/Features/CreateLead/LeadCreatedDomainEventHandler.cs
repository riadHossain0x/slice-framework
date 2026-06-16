using Microsoft.Extensions.Logging;
using Slice.EventBus;
using Slice.Sample.Crm.Domain.Leads.Events;

namespace Slice.Sample.Crm.Features.CreateLead;

/// <summary>Demonstrates local domain-event handling — runs after the lead is persisted.</summary>
public sealed class LeadCreatedDomainEventHandler(ILogger<LeadCreatedDomainEventHandler> logger)
    : IDomainEventHandler<LeadCreatedDomainEvent>
{
    public Task HandleAsync(LeadCreatedDomainEvent @event, CancellationToken ct)
    {
        logger.LogInformation("Lead created: {LeadId} ({FullName})", @event.LeadId, @event.FullName);
        return Task.CompletedTask;
    }
}
