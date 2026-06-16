using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Slice.Core.Ambient;

namespace Slice.MultiTenancy;

/// <summary>
/// Resolves the tenant for the request and pushes it onto the ambient <see cref="ICurrentTenant"/>
/// for the duration of the pipeline, so handlers and EF query filters see the right tenant.
/// </summary>
public sealed class MultiTenancyMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantResolver resolver, ICurrentTenant currentTenant)
    {
        var result = await resolver.ResolveAsync(context.RequestAborted);
        using (currentTenant.Change(result.TenantId))
        {
            await next(context);
        }
    }
}

public static class MultiTenancyMiddlewareExtensions
{
    public static IApplicationBuilder UseSliceMultiTenancy(this IApplicationBuilder app)
        => app.UseMiddleware<MultiTenancyMiddleware>();
}
