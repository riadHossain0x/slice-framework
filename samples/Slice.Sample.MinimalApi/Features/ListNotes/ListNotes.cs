using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Slice.Application;
using Slice.AspNetCore.MinimalApi;
using Slice.Authorization;
using Slice.Core.Results;
using Slice.Mediator;
using Slice.Sample.MinimalApi.Domain;
using Slice.Sample.MinimalApi.Permissions;
using Slice.Sample.MinimalApi.ReadModels;

namespace Slice.Sample.MinimalApi.Features.ListNotes;

[SlicePermission(NotesPermissions.View)]
public sealed record ListNotesQuery : IQuery<Result<IReadOnlyList<NoteDto>>>;

public sealed class ListNotesHandler(INoteRepository repository)
    : IQueryHandler<ListNotesQuery, Result<IReadOnlyList<NoteDto>>>
{
    public async Task<Result<IReadOnlyList<NoteDto>>> HandleAsync(ListNotesQuery query, CancellationToken ct)
    {
        var notes = await repository.GetListAsync(specification: null, ct);
        IReadOnlyList<NoteDto> dtos = notes
            .Select(n => new NoteDto(n.Id, n.Title, n.Body, n.CreationTime, n.ConcurrencyStamp))
            .ToList();
        return Result<IReadOnlyList<NoteDto>>.Success(dtos);
    }
}

public sealed class ListNotesEndpoint : ISliceEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
        => endpoints.MapGet("/v1/notes", (ISender sender, CancellationToken ct)
                => sender.SendAsync(new ListNotesQuery(), ct))
            .WithName("ListNotes");
}
