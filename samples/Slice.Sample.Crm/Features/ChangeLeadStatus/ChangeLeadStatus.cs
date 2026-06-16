using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Slice.Application;
using Slice.AspNetCore.ConditionalRequests;
using Slice.AspNetCore.Mvc;
using Slice.Authorization;
using Slice.Core.Results;
using Slice.ObjectMapping;
using Slice.Sample.Crm.Domain.Leads;
using Slice.Sample.Crm.Permissions;
using Slice.Sample.Crm.Persistence;
using Slice.Sample.Crm.ReadModels;

namespace Slice.Sample.Crm.Features.ChangeLeadStatus;

// Demonstrates optimistic concurrency: the caller sends the version it last saw as If-Match; the handler
// pins it onto the aggregate's concurrency token. If another writer moved the lead on, SaveChanges raises
// DbUpdateConcurrencyException, which the conditional-request middleware maps to 412.
[SlicePermission(CrmPermissions.Leads.Edit)]
public sealed record ChangeLeadStatusCommand(Guid Id, LeadStatus Status, string? IfMatch) : ICommand<Result<LeadDto>>;

public sealed class ChangeLeadStatusHandler(ILeadRepository repository, CrmDbContext db, IObjectMapper mapper)
    : ICommandHandler<ChangeLeadStatusCommand, Result<LeadDto>>
{
    public async Task<Result<LeadDto>> HandleAsync(ChangeLeadStatusCommand command, CancellationToken ct)
    {
        var lead = await repository.FindAsync(command.Id, ct);
        if (lead is null)
            return Error.NotFound("Crm:Lead.NotFound", $"Lead '{command.Id}' was not found.");

        lead.ChangeStatus(command.Status);

        // Enforce the client's If-Match (no-op when the header is absent).
        db.Entry(lead).UseIfMatch(command.IfMatch);

        return mapper.Map<Lead, LeadDto>(lead);
    }
}

public sealed record ChangeLeadStatusRequest(LeadStatus Status);

[Authorize]
[Route("api/crm/leads")]
public sealed class ChangeLeadStatusController : SliceController
{
    [HttpPatch("{id:guid}/status")]
    public Task<IActionResult> ChangeStatus(Guid id, [FromBody] ChangeLeadStatusRequest body, CancellationToken ct)
        => SendAsync(new ChangeLeadStatusCommand(id, body.Status, HttpContext.GetIfMatch()), ct);
}
