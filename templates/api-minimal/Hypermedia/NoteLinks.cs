using Slice.AspNetCore.Hypermedia;
using SliceMinimalApp.Permissions;
using SliceMinimalApp.ReadModels;

namespace SliceMinimalApp.Hypermedia;

/// <summary>HAL links for a <see cref="NoteDto"/> (content-negotiated, permission-aware).</summary>
public sealed class NoteLinks : IResourceLinkContributor<NoteDto>
{
    public async Task ContributeAsync(NoteDto note, LinkBuilder links, CancellationToken ct)
    {
        links.EmbeddedRel = "notes";
        links.AddRoute("self", "GetNote", new { id = note.Id });   // minimal-API named routes
        links.AddRoute("list", "ListNotes");
        await Task.CompletedTask;
    }
}
