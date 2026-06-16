using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Slice.Application;
using Slice.AspNetCore.ConditionalRequests;
using Slice.AspNetCore.MinimalApi;
using Slice.Authorization;
using Slice.Core.Results;
using Slice.Mediator;
using Slice.Sample.MinimalApi.Domain;
using Slice.Sample.MinimalApi.Permissions;
using Slice.Sample.MinimalApi.Persistence;
using Slice.Sample.MinimalApi.ReadModels;

namespace Slice.Sample.MinimalApi.Features.UpdateNote;

[SlicePermission(NotesPermissions.Create)]
public sealed record UpdateNoteCommand(Guid Id, string Title, string Body, string? IfMatch) : ICommand<Result<NoteDto>>;

public sealed class UpdateNoteValidator : AbstractValidator<UpdateNoteCommand>
{
    public UpdateNoteValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Body).NotEmpty();
    }
}

public sealed class UpdateNoteHandler(INoteRepository repository, NotesDbContext db)
    : ICommandHandler<UpdateNoteCommand, Result<NoteDto>>
{
    public async Task<Result<NoteDto>> HandleAsync(UpdateNoteCommand command, CancellationToken ct)
    {
        var note = await repository.FindAsync(command.Id, ct);
        if (note is null)
            return Error.NotFound("Notes:NotFound", $"Note '{command.Id}' was not found.");

        note.Update(command.Title, command.Body);
        db.Entry(note).UseIfMatch(command.IfMatch);   // stale If-Match → DbUpdateConcurrencyException → 412
        return new NoteDto(note.Id, note.Title, note.Body, note.CreationTime, note.ConcurrencyStamp);
    }
}

public sealed record UpdateNoteRequest(string Title, string Body);

public sealed class UpdateNoteEndpoint : ISliceEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
        => endpoints.MapPut("/v1/notes/{id:guid}", (Guid id, UpdateNoteRequest body, HttpContext http, ISender sender, CancellationToken ct)
                => sender.SendAsync(new UpdateNoteCommand(id, body.Title, body.Body, http.GetIfMatch()), ct))
            .WithName("UpdateNote")
            .Produces<NoteDto>()
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesValidationProblem();
}
