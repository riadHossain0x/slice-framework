using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Slice.Application;
using Slice.AspNetCore.MinimalApi;
using Slice.Core.Results;
using Slice.Mediator;
using Slice.Sample.MinimalApi.Domain;
using Slice.Sample.MinimalApi.ReadModels;

namespace Slice.Sample.MinimalApi.Features.GetNote;

public sealed record GetNoteQuery(Guid Id) : IQuery<Result<NoteDto>>;

public sealed class GetNoteHandler(INoteRepository repository) : IQueryHandler<GetNoteQuery, Result<NoteDto>>
{
    public async Task<Result<NoteDto>> HandleAsync(GetNoteQuery query, CancellationToken ct)
    {
        var note = await repository.FindAsync(query.Id, ct);
        if (note is null)
            return Error.NotFound("Notes:NotFound", $"Note '{query.Id}' was not found.");

        return new NoteDto(note.Id, note.Title, note.Body, note.CreationTime, note.ConcurrencyStamp);
    }
}

public sealed class GetNoteEndpoint : ISliceEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
        => endpoints.MapGet("/v1/notes/{id:guid}", (Guid id, ISender sender, CancellationToken ct)
                => sender.SendAsync(new GetNoteQuery(id), ct))
            .WithName("GetNote")
            .Produces<NoteDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);
}
