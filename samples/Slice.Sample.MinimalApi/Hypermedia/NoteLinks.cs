using Slice.AspNetCore.Hypermedia;
using Slice.Sample.MinimalApi.Permissions;
using Slice.Sample.MinimalApi.ReadModels;

namespace Slice.Sample.MinimalApi.Hypermedia;

/// <summary>HAL links for a <see cref="NoteDto"/>. Same contributor model the controllers use.</summary>
public sealed class NoteLinks : IResourceLinkContributor<NoteDto>
{
    public async Task ContributeAsync(NoteDto note, LinkBuilder links, CancellationToken ct)
    {
        links.EmbeddedRel = "notes";
        links.AddRoute("self", "GetNote", new { id = note.Id });               // minimal-API named routes
        links.AddRoute("list", "ListNotes");
        await links.AddRouteIfGranted(NotesPermissions.Create, "update", "UpdateNote", new { id = note.Id }, "PUT", ct);
    }
}
