using Microsoft.AspNetCore.Mvc;
using Slice.Application;
using Slice.AspNetCore.Mvc;
using Slice.Authorization;
using Slice.Core.Results;
using Slice.Sample.Crm.Domain.Leads;
using Slice.Sample.Crm.Permissions;
using Slice.Sample.Crm.ReadModels;

namespace Slice.Sample.Crm.Features.ListLeads;

[SlicePermission(CrmPermissions.Leads.View)]
public sealed record ListLeadsQuery : IQuery<Result<IReadOnlyList<LeadDto>>>;

public sealed class ListLeadsHandler(ILeadRepository repository)
    : IQueryHandler<ListLeadsQuery, Result<IReadOnlyList<LeadDto>>>
{
    public async Task<Result<IReadOnlyList<LeadDto>>> HandleAsync(ListLeadsQuery query, CancellationToken ct)
    {
        // Goes through the EF query filters → only the current tenant's non-deleted leads.
        var leads = await repository.GetListAsync(specification: null, ct);
        IReadOnlyList<LeadDto> dtos = leads
            .Select(l => new LeadDto(l.Id, l.Name.DisplayName, l.Contact.Email, l.Contact.Phone, l.Status, l.Source, l.CreationTime, l.CreatorId, l.ConcurrencyStamp, l.LastModificationTime))
            .ToList();
        return Result<IReadOnlyList<LeadDto>>.Success(dtos);
    }
}

[Microsoft.AspNetCore.Authorization.Authorize]
[Route("api/crm/leads")]
public sealed class ListLeadsController : SliceController
{
    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct) => SendAsync(new ListLeadsQuery(), ct);
}
