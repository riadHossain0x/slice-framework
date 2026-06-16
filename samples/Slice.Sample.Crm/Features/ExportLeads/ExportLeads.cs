using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Slice.Application;
using Slice.AspNetCore.Mvc;
using Slice.Authorization;
using Slice.Core.Results;
using Slice.Sample.Crm.Domain.Leads;
using Slice.Sample.Crm.Permissions;

namespace Slice.Sample.Crm.Features.ExportLeads;

// Gated by Crm.Leads.Export — a permission that is NOT granted by default, so it cleanly demonstrates
// 403 → grant → 200 for a non-admin caller. The check runs in the pipeline (AuthorizationBehavior),
// before this handler, for any caller — HTTP or background.
[SlicePermission(CrmPermissions.Leads.Export)]
public sealed record ExportLeadsQuery : IQuery<Result<string>>;

public sealed class ExportLeadsHandler(ILeadRepository repository)
    : IQueryHandler<ExportLeadsQuery, Result<string>>
{
    public async Task<Result<string>> HandleAsync(ExportLeadsQuery query, CancellationToken ct)
    {
        var leads = await repository.GetListAsync(specification: null, ct);

        var csv = new StringBuilder("Name,Email,Phone,Status,Source\n");
        foreach (var lead in leads)
            csv.Append($"{lead.Name.DisplayName},{lead.Contact.Email},{lead.Contact.Phone},{lead.Status},{lead.Source}\n");

        return Result<string>.Success(csv.ToString());
    }
}

[Authorize]
[Route("api/crm/leads/export")]
public sealed class ExportLeadsController : SliceController
{
    [HttpGet]
    public Task<IActionResult> Export(CancellationToken ct) => SendAsync(new ExportLeadsQuery(), ct);
}
