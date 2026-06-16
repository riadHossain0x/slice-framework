using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Slice.Application;
using Slice.AspNetCore.MinimalApi;
using Slice.Authorization;
using Slice.Core.Ambient;
using Slice.Core.Results;
using Slice.Mediator;
using Slice.Sample.MinimalApi.Domain;
using Slice.Sample.MinimalApi.Permissions;

namespace Slice.Sample.MinimalApi.Features.CreateNote;

[SlicePermission(NotesPermissions.Create)]
public sealed record CreateNoteCommand(string Title, string Body) : ICommand<Result<Guid>>;

public sealed class CreateNoteValidator : AbstractValidator<CreateNoteCommand>
{
    public CreateNoteValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Body).NotEmpty();
    }
}

public sealed class CreateNoteHandler(INoteRepository repository, IGuidGenerator guids)
    : ICommandHandler<CreateNoteCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(CreateNoteCommand command, CancellationToken ct)
    {
        var note = new Note(guids.Create(), command.Title, command.Body);
        await repository.InsertAsync(note, autoSave: false, ct);   // UnitOfWorkBehavior commits on success
        return Result<Guid>.Success(note.Id);
    }
}

public sealed class CreateNoteEndpoint : ISliceEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
        => endpoints.MapPost("/v1/notes", (CreateNoteCommand command, ISender sender, CancellationToken ct)
                => sender.SendAsync(command, ct))
            .WithName("CreateNote")
            .Produces<Guid>(StatusCodes.Status200OK)
            .ProducesValidationProblem();
}
