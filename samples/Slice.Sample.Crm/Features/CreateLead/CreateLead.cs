using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Slice.Application;
using Slice.AspNetCore.Mvc;
using Slice.Authorization;
using Slice.Core.Ambient;
using Slice.Core.Results;
using Slice.Sample.Crm.Domain.Leads;
using Slice.Sample.Crm.Permissions;

namespace Slice.Sample.Crm.Features.CreateLead;

[SlicePermission(CrmPermissions.Leads.Create)]
public sealed record CreateLeadCommand(
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    LeadSource Source) : ICommand<Result<Guid>>;

public sealed class CreateLeadValidator : AbstractValidator<CreateLeadCommand>
{
    public CreateLeadValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(128);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Email) || !string.IsNullOrWhiteSpace(x.Phone))
            .WithMessage("A lead must have an email or a phone number.");
    }
}

public sealed class CreateLeadHandler(ILeadRepository repository, IGuidGenerator guids, ICurrentTenant tenant)
    : ICommandHandler<CreateLeadCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(CreateLeadCommand command, CancellationToken ct)
    {
        var lead = new Lead(
            guids.Create(),
            tenant.Id,
            FullName.Create(command.FirstName, command.LastName),
            ContactInfo.Create(command.Email, command.Phone),
            command.Source);

        // autoSave:false — the UnitOfWorkBehavior commits after the command succeeds,
        // which also triggers the audit + domain-event interceptors.
        await repository.InsertAsync(lead, autoSave: false, ct);
        return Result<Guid>.Success(lead.Id);
    }
}

[Microsoft.AspNetCore.Authorization.Authorize]
[Route("api/crm/leads")]
public sealed class CreateLeadController : SliceController
{
    [HttpPost]
    public Task<IActionResult> Create([FromBody] CreateLeadCommand command, CancellationToken ct)
        => SendAsync(command, ct);
}
