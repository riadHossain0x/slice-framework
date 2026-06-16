using Microsoft.AspNetCore.Mvc;
using Slice.Application;
using Slice.AspNetCore.Mvc;
using Slice.Core.Results;
using Slice.Features;

namespace Slice.Sample.Crm.Features.Beta;

public static class CrmFeatures
{
    public const string Beta = "Crm.Beta";
}

public sealed class CrmFeatureDefinitionProvider : FeatureDefinitionProvider
{
    public override void Define(IFeatureDefinitionContext context)
        => context.Add(new FeatureDefinition(CrmFeatures.Beta, defaultValue: "false", displayName: "Beta features"));
}

[RequiresFeature(CrmFeatures.Beta)]
public sealed record GetBetaQuery : IQuery<Result<string>>;

public sealed class GetBetaHandler : IQueryHandler<GetBetaQuery, Result<string>>
{
    public Task<Result<string>> HandleAsync(GetBetaQuery query, CancellationToken ct)
        => Task.FromResult(Result<string>.Success("beta enabled"));
}

[Route("api/crm/beta")]
public sealed class BetaController : SliceController
{
    [HttpGet]
    public Task<IActionResult> Get(CancellationToken ct) => SendAsync(new GetBetaQuery(), ct);
}
