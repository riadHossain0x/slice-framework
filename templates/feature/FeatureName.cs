using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Slice.Application;
using Slice.AspNetCore.Mvc;
using Slice.Core.Results;

namespace ModuleName.Features.FeatureName;

public sealed record FeatureNameCommand(string Name) : ICommand<Result<Guid>>;

public sealed class FeatureNameValidator : AbstractValidator<FeatureNameCommand>
{
    public FeatureNameValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
    }
}

public sealed class FeatureNameHandler : ICommandHandler<FeatureNameCommand, Result<Guid>>
{
    public Task<Result<Guid>> HandleAsync(FeatureNameCommand command, CancellationToken ct)
    {
        // TODO: implement the use case.
        return Task.FromResult(Result<Guid>.Success(Guid.NewGuid()));
    }
}

[Route("api/featurename")]
public sealed class FeatureNameController : SliceController
{
    [HttpPost]
    public Task<IActionResult> Post([FromBody] FeatureNameCommand command, CancellationToken ct)
        => SendAsync(command, ct);
}
