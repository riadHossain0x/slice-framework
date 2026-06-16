using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Slice.Application;
using Slice.AspNetCore.Mvc;
using Slice.Authorization;
using Slice.Core.Results;
using SliceApp.Domain;
using SliceApp.Permissions;

namespace SliceApp.Features.ListNotes;

public sealed record NoteDto(Guid Id, string Title, string Body, DateTime CreationTime, Guid? CreatorId);

[SlicePermission(AppPermissions.Notes.View)]
public sealed record ListNotesQuery : IQuery<Result<IReadOnlyList<NoteDto>>>;

public sealed class ListNotesHandler(INoteRepository repository)
    : IQueryHandler<ListNotesQuery, Result<IReadOnlyList<NoteDto>>>
{
    public async Task<Result<IReadOnlyList<NoteDto>>> HandleAsync(ListNotesQuery query, CancellationToken ct)
    {
        // Goes through the EF query filters → only the current tenant's non-deleted notes.
        var notes = await repository.GetListAsync(specification: null, ct);
        IReadOnlyList<NoteDto> dtos = notes
            .Select(n => new NoteDto(n.Id, n.Title, n.Body, n.CreationTime, n.CreatorId))
            .ToList();
        return Result<IReadOnlyList<NoteDto>>.Success(dtos);
    }
}

[Authorize]
[Route("api/notes")]
public sealed class ListNotesController : SliceController
{
    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct) => SendAsync(new ListNotesQuery(), ct);
}
