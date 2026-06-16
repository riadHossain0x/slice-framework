using Slice.AspNetCore.Hypermedia;
using Slice.Sample.Crm.Permissions;
using Slice.Sample.Crm.ReadModels;

namespace Slice.Sample.Crm.Hypermedia;

/// <summary>
/// Hypermedia links for a <see cref="LeadDto"/>. <c>self</c> and <c>list</c> are always present; the
/// state-changing affordances are emitted only when the caller holds the matching permission — so a
/// read-only user sees a lead without <c>update</c>/<c>export</c> links. Hrefs resolve against the
/// feature controllers (attribute routing) by action + controller name.
/// </summary>
public sealed class LeadLinks : IResourceLinkContributor<LeadDto>
{
    public async Task ContributeAsync(LeadDto lead, LinkBuilder links, CancellationToken ct)
    {
        links.EmbeddedRel = "leads";

        links.Self("Get", "GetLead", new { id = lead.Id });
        links.Add("list", "List", "ListLeads");

        await links.AddIfGranted(
            CrmPermissions.Leads.Edit, "update", "ChangeStatus", "ChangeLeadStatus",
            new { id = lead.Id }, method: "PATCH", ct: ct);

        await links.AddIfGranted(
            CrmPermissions.Leads.Export, "export", "Export", "ExportLeads", ct: ct);
    }
}
