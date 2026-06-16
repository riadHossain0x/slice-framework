using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Slice.AspNetCore.MinimalApi;
using Slice.Mediator;
using Slice.Sample.Crm.Features.CreateLead;
using Slice.Sample.Crm.Features.GetLead;
using Slice.Sample.Crm.Features.ListLeads;
using Slice.Sample.Crm.ReadModels;

namespace Slice.Sample.Crm.MinimalApi;

/// <summary>
/// Minimal-API surface for leads, mapped under <c>/api/min/crm/leads</c>. These endpoints dispatch the
/// SAME commands/queries the controllers use — proving controllers and minimal APIs share one pipeline
/// (validation, <c>[SlicePermission]</c>, multitenancy, unit-of-work) and one set of HAL link contributors.
/// Discovered automatically by <c>app.MapSliceEndpoints(...)</c> — no central wiring per feature.
/// </summary>
public sealed class LeadEndpoints : ISliceEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        var leads = endpoints.MapGroup("/api/min/crm/leads").WithTags("Leads (minimal API)");

        // GET one — HAL + version-ETag apply (configured on the parent group).
        leads.MapGet("/{id:guid}", (Guid id, ISender sender, CancellationToken ct)
                => sender.SendAsync(new GetLeadQuery(id), ct))
            .WithName("MinGetLead")
            .Produces<LeadDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        // GET list — gated by Crm.Leads.View in the pipeline.
        leads.MapGet("/", (ISender sender, CancellationToken ct)
                => sender.SendAsync(new ListLeadsQuery(), ct))
            .WithName("MinListLeads");

        // POST — validated + gated by Crm.Leads.Create in the pipeline; the body binds to the command.
        leads.MapPost("/", (CreateLeadCommand command, ISender sender, CancellationToken ct)
                => sender.SendAsync(command, ct))
            .WithName("MinCreateLead")
            .Produces<Guid>(StatusCodes.Status200OK)
            .ProducesValidationProblem();
    }
}
