using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Slice.Application;
using Slice.AspNetCore.Mvc;
using Slice.Authorization;
using Slice.Core.Ambient;
using Slice.Core.Results;
using ModuleName.Domain;
using ModuleName.Permissions;

namespace ModuleName.Features.CreateItem;

[SlicePermission(ModuleNamePermissions.Items.Create)]
public sealed record CreateItemCommand(string Name) : ICommand<Result<Guid>>;

public sealed class CreateItemValidator : AbstractValidator<CreateItemCommand>
{
    public CreateItemValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
}

public sealed class CreateItemHandler(IItemRepository repository, IGuidGenerator guids, ICurrentTenant tenant)
    : ICommandHandler<CreateItemCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(CreateItemCommand command, CancellationToken ct)
    {
        var item = new Item(guids.Create(), tenant.Id, command.Name);
        // autoSave:false — the UnitOfWorkBehavior commits after the command succeeds.
        await repository.InsertAsync(item, autoSave: false, ct);
        return Result<Guid>.Success(item.Id);
    }
}

[Authorize]
[Route("api/modulename/items")]
public sealed class CreateItemController : SliceController
{
    [HttpPost]
    public Task<IActionResult> Create([FromBody] CreateItemCommand command, CancellationToken ct)
        => SendAsync(command, ct);
}
