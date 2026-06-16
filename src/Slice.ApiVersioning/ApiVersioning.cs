using Asp.Versioning;
using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Slice.ApiVersioning;

public static class SliceApiVersioningRegistration
{
    /// <summary>
    /// Enables API versioning (default v1.0, assumed when unspecified) read from the URL segment
    /// <c>v{version}</c> or the <c>X-Api-Version</c> header, with supported versions reported in responses.
    /// Configures both MVC controllers and minimal-API endpoints (the latter use version sets — see
    /// <see cref="NewSliceApiVersionSet"/>).
    /// </summary>
    public static IServiceCollection AddSliceApiVersioning(this IServiceCollection services)
    {
        services
            .AddApiVersioning(options =>
            {
                options.DefaultApiVersion = new ApiVersion(1, 0);
                options.AssumeDefaultVersionWhenUnspecified = true;
                options.ReportApiVersions = true;
                options.ApiVersionReader = ApiVersionReader.Combine(
                    new UrlSegmentApiVersionReader(),
                    new HeaderApiVersionReader("X-Api-Version"));
            })
            .AddMvc();

        return services;
    }

    /// <summary>
    /// Builds an <see cref="ApiVersionSet"/> for minimal-API route groups. Attach it with
    /// <c>group.WithApiVersionSet(set)</c> and tag endpoints via <c>.MapToApiVersion(1.0)</c>; combine with a
    /// <c>v{version:apiVersion}</c> route prefix for URL-segment versioning.
    /// </summary>
    public static ApiVersionSet NewSliceApiVersionSet(this IEndpointRouteBuilder endpoints, params double[] versions)
    {
        var builder = endpoints.NewApiVersionSet();
        foreach (var version in versions)
            builder.HasApiVersion(new ApiVersion(version));
        return builder.ReportApiVersions().Build();
    }
}
