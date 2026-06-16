using Microsoft.Extensions.Logging;
using Slice.BackgroundJobs;
using Slice.EventBus;
using Slice.Sample.Crm.Domain.Leads.Events;

namespace Slice.Sample.Crm.Features.CreateLead;

/// <summary>
/// Distributed (integration) handler — invoked asynchronously by the outbox processor after the
/// lead is committed. Here it enqueues a background job, showing the full chain:
/// command → UoW commit → outbox → distributed handler → background job.
/// </summary>
public sealed class LeadCreatedEtoHandler(IBackgroundJobManager jobs, ILogger<LeadCreatedEtoHandler> logger)
    : IDistributedEventHandler<LeadCreatedEto>
{
    public async Task HandleAsync(LeadCreatedEto @event, CancellationToken ct)
    {
        logger.LogInformation("OUTBOX: lead {LeadId} created; queuing welcome email", @event.LeadId);
        await jobs.EnqueueAsync(new WelcomeEmailArgs(@event.LeadId, @event.Email));
    }
}
