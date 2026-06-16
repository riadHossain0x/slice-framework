using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Slice.Application;
using Slice.AspNetCore.Mvc;
using Slice.Caching;
using Slice.Core.Results;
using Slice.Localization;
using Slice.Sample.Crm.Localization;
using Slice.Sample.Crm.Settings;
using Slice.Settings;

namespace Slice.Sample.Crm.Features.GetCrmInfo;

public sealed record CrmInfoDto(int AutoArchiveDays, string Greeting);

public sealed record GetCrmInfoQuery : IQuery<Result<CrmInfoDto>>;

public sealed class GetCrmInfoHandler(
    ISettingManager settings, ISliceLocalizer localizer, ISliceCache cache, ILogger<GetCrmInfoHandler> logger)
    : IQueryHandler<GetCrmInfoQuery, Result<CrmInfoDto>>
{
    public async Task<Result<CrmInfoDto>> HandleAsync(GetCrmInfoQuery query, CancellationToken ct)
    {
        // Cached (tenant-aware); the factory runs only on a miss.
        var days = await cache.GetOrAddAsync("crm:autoArchiveDays", async () =>
        {
            logger.LogInformation("CACHE-MISS: computing autoArchiveDays");
            return await settings.GetAsync<int>(CrmSettings.AutoArchiveDays);
        }, TimeSpan.FromMinutes(5), ct);

        return new CrmInfoDto(days, localizer[CrmKeys.Greeting]);
    }
}

// Anonymous: demonstrates settings + localization without a token.
[Route("api/crm/info")]
public sealed class GetCrmInfoController : SliceController
{
    [HttpGet]
    public Task<IActionResult> Get(CancellationToken ct) => SendAsync(new GetCrmInfoQuery(), ct);
}
