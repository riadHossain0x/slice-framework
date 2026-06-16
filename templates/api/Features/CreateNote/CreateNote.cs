using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Slice.Application;
using Slice.AspNetCore.Mvc;
using Slice.Authorization;
using Slice.Core.Ambient;
using Slice.Core.Results;
using SliceApp.Domain;
using SliceApp.Permissions;

namespace SliceApp.Features.CreateNote;

[SlicePermission(AppPermissions.Notes.Create)]
public sealed record CreateNoteCommand(string Title, string Body) : ICommand<Result<Guid>>;

public sealed class CreateNoteValidator : AbstractValidator<CreateNoteCommand>
{
    public CreateNoteValidator() => RuleFor(x => x.Title).NotEmpty().MaximumLength(256);
}

public sealed class CreateNoteHandler(INoteRepository repository, IGuidGenerator guids, ICurrentTenant tenant)
    : ICommandHandler<CreateNoteCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(CreateNoteCommand command, CancellationToken ct)
    {
        var note = new Note(guids.Create(), tenant.Id, command.Title, command.Body);
        // autoSave:false — the UnitOfWorkBehavior commits after the command succeeds.
        await repository.InsertAsync(note, autoSave: false, ct);
        return Result<Guid>.Success(note.Id);
    }
}

[Authorize]
[Route("api/notes")]
public sealed class CreateNoteController : SliceController
{
    [HttpPost]
    public Task<IActionResult> Create([FromBody] CreateNoteCommand command, CancellationToken ct)
        => SendAsync(command, ct);
}
