using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Scalar.AspNetCore;

namespace Slice.AspNetCore;

/// <summary>
/// Thin wrappers over .NET's built-in OpenAPI (<c>Microsoft.AspNetCore.OpenApi</c>) plus a
/// <see href="https://scalar.com">Scalar</see> reference UI, so any Slice host — controller-based or
/// minimal-API — opts into both an OpenAPI document and an interactive UI with one line each.
/// Endpoints describe themselves with the standard metadata (controller attributes, or minimal-API
/// <c>WithName</c>/<c>WithTags</c>/<c>Produces&lt;T&gt;</c>/<c>ProducesProblem</c>).
/// </summary>
public static class SliceOpenApiExtensions
{
    /// <summary>Registers the OpenAPI document services (default document name <c>"v1"</c>).</summary>
    public static IServiceCollection AddSliceOpenApi(this IServiceCollection services, string documentName = "v1")
        => services.AddOpenApi(documentName);

    /// <summary>
    /// Maps the OpenAPI document endpoint (default <c>/openapi/v1.json</c>) and, when
    /// <paramref name="mapUi"/> is <see langword="true"/> (the default), the Scalar reference UI
    /// (default <c>/scalar/v1</c>). Pass <c>mapUi: false</c> to expose only the JSON document.
    /// </summary>
    public static IEndpointRouteBuilder MapSliceOpenApi(this IEndpointRouteBuilder endpoints, bool mapUi = true)
    {
        endpoints.MapOpenApi();
        if (mapUi)
            endpoints.MapScalarApiReference();
        return endpoints;
    }
}
