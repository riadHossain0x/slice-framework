using Microsoft.AspNetCore.Mvc;
using Slice.Application;
using Slice.AspNetCore.Mvc;
using Slice.Core.Results;
using Slice.ObjectMapping;
using Slice.Sample.Crm.Domain.Leads;
using Slice.Sample.Crm.ReadModels;

namespace Slice.Sample.Crm.Features.GetLead;

public sealed record GetLeadQuery(Guid Id) : IQuery<Result<LeadDto>>;

public sealed class GetLeadHandler(ILeadRepository repository, IObjectMapper mapper)
    : IQueryHandler<GetLeadQuery, Result<LeadDto>>
{
    public async Task<Result<LeadDto>> HandleAsync(GetLeadQuery query, CancellationToken ct)
    {
        var lead = await repository.FindAsync(query.Id, ct);
        if (lead is null)
            return Error.NotFound("Crm:Lead.NotFound", $"Lead '{query.Id}' was not found.");

        return mapper.Map<Lead, LeadDto>(lead);
    }
}

[Route("api/crm/leads")]
public sealed class GetLeadController : SliceController
{
    [HttpGet("{id:guid}")]
    public Task<IActionResult> Get(Guid id, CancellationToken ct)
        => SendAsync(new GetLeadQuery(id), ct);
}
