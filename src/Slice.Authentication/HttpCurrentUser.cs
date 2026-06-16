using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OpenIddict.Abstractions;
using Slice.Core.Ambient;
using Slice.Core.DependencyInjection;

namespace Slice.Authentication;

/// <summary>
/// <see cref="ICurrentUser"/> backed by the request principal. Reads the OpenIddict <c>sub</c>
/// claim for the id, name, and role claims. Singleton-safe (only depends on the singleton
/// <see cref="IHttpContextAccessor"/>), so the singleton auditing interceptor can keep using it.
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser, ISingletonDependency
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public Guid? Id
    {
        get
        {
            var value = Principal?.FindFirst(OpenIddictConstants.Claims.Subject)?.Value
                        ?? Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public string? UserName =>
        Principal?.FindFirst(OpenIddictConstants.Claims.Name)?.Value
        ?? Principal?.Identity?.Name;

    public string[] Roles =>
        Principal?.FindAll(OpenIddictConstants.Claims.Role).Select(c => c.Value)
            .Concat(Principal.FindAll(ClaimTypes.Role).Select(c => c.Value))
            .Distinct()
            .ToArray()
        ?? [];
}
