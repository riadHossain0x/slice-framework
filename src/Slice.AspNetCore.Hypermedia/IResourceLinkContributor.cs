using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Slice.Authorization;

namespace Slice.AspNetCore.Hypermedia;

/// <summary>
/// Declares the hypermedia links for a resource type. Implement one per DTO you want to enrich and
/// register it via <c>AddResourceLinkContributors(assembly)</c>; the <see cref="HalResourceFilter"/>
/// resolves the contributor for the response's runtime type and invokes it. Links are built through
/// <see cref="LinkBuilder"/>, which resolves hrefs from the route table and can hide a link the caller
/// is not permitted to follow.
/// </summary>
/// <typeparam name="TResource">The resource (DTO) type this contributor produces links for.</typeparam>
public interface IResourceLinkContributor<in TResource>
{
    Task ContributeAsync(TResource resource, LinkBuilder links, CancellationToken ct = default);
}

/// <summary>
/// Collects a resource's links during contribution. A link can be targeted three ways:
/// <list type="bullet">
/// <item><b>action + controller</b> (<see cref="Add"/>/<see cref="Self"/>) — resolved against the route
/// table via <see cref="LinkGenerator.GetUriByAction"/>; the default for attribute-routed controllers.</item>
/// <item><b>route name</b> (<see cref="AddRoute"/>/<see cref="SelfRoute"/>) — resolved via
/// <see cref="LinkGenerator.GetUriByName"/> for complex/parameterized routes that opt in with
/// <c>[Http..(Name = "…")]</c>; still produces an absolute URL with route-value substitution.</item>
/// <item><b>literal href</b> (<see cref="AddHref"/>/<see cref="SelfHref"/>) — emitted verbatim, for fully
/// custom or external/gateway-prefixed URLs that <see cref="LinkGenerator"/> can't resolve.</item>
/// </list>
/// The <c>*IfGranted</c> variants emit a link only when the current caller holds the given permission —
/// permission results are memoized per request.
/// </summary>
public sealed class LinkBuilder
{
    private readonly HttpContext _http;
    private readonly LinkGenerator _links;
    private readonly IPermissionChecker _permissions;
    private readonly Dictionary<string, bool> _permissionCache;

    internal LinkBuilder(
        HttpContext http,
        LinkGenerator links,
        IPermissionChecker permissions,
        Dictionary<string, bool> permissionCache)
    {
        _http = http;
        _links = links;
        _permissions = permissions;
        _permissionCache = permissionCache;
    }

    /// <summary>The links accumulated so far, keyed by relation.</summary>
    public IDictionary<string, HalLink> Links { get; } = new Dictionary<string, HalLink>(StringComparer.Ordinal);

    /// <summary>
    /// The <c>_embedded</c> relation under which a collection's items are nested (collections only).
    /// Defaults to <c>"items"</c>; set it from the contributor to use a domain name (e.g. <c>"leads"</c>).
    /// </summary>
    public string EmbeddedRel { get; set; } = "items";

    /// <summary>Adds the reserved <c>self</c> link pointing at the given action.</summary>
    public void Self(string action, string controller, object? values = null)
        => Add(Hal.Self, action, controller, values);

    /// <summary>Adds a link with the given relation, resolving its href from the action+controller.</summary>
    public void Add(string rel, string action, string controller, object? values = null, string? method = null)
    {
        var href = _links.GetUriByAction(_http, action, controller, values);
        if (!string.IsNullOrEmpty(href))
            Links[rel] = new HalLink(href, method);
    }

    /// <summary>Adds the reserved <c>self</c> link by resolving a named route (see <see cref="AddRoute"/>).</summary>
    public void SelfRoute(string routeName, object? values = null)
        => AddRoute(Hal.Self, routeName, values);

    /// <summary>
    /// Adds a link by resolving a registered route by its name (via <see cref="LinkGenerator.GetUriByName"/>) —
    /// for complex/parameterized routes the endpoint exposes with <c>[Http..(Name = "…")]</c>. The link is
    /// omitted if the name resolves to nothing.
    /// </summary>
    public void AddRoute(string rel, string routeName, object? values = null, string? method = null)
    {
        var href = _links.GetUriByName(_http, routeName, values);
        if (!string.IsNullOrEmpty(href))
            Links[rel] = new HalLink(href, method);
    }

    /// <summary>Adds a named-route link only when the current caller is granted <paramref name="permission"/>.</summary>
    public async Task AddRouteIfGranted(
        string permission, string rel, string routeName,
        object? values = null, string? method = null, CancellationToken ct = default)
    {
        if (await IsGrantedAsync(permission, ct))
            AddRoute(rel, routeName, values, method);
    }

    /// <summary>Adds the reserved <c>self</c> link from a literal href (see <see cref="AddHref"/>).</summary>
    public void SelfHref(string href, bool templated = false)
        => AddHref(Hal.Self, href, templated: templated);

    /// <summary>
    /// Adds a link from a literal href, emitted verbatim — for fully custom or external/gateway-prefixed
    /// URLs that <see cref="LinkGenerator"/> can't resolve. The caller owns the href's correctness.
    /// </summary>
    public void AddHref(string rel, string href, string? method = null, bool templated = false)
        => AddLink(rel, new HalLink(href, method, Templated: templated));

    /// <summary>Adds a literal-href link only when the current caller is granted <paramref name="permission"/>.</summary>
    public async Task AddHrefIfGranted(
        string permission, string rel, string href,
        string? method = null, bool templated = false, CancellationToken ct = default)
    {
        if (await IsGrantedAsync(permission, ct))
            AddHref(rel, href, method, templated);
    }

    /// <summary>Adds a literal link (href already known) under the given relation.</summary>
    public void AddLink(string rel, HalLink link) => Links[rel] = link;

    /// <summary>
    /// Adds a link only when the current caller is granted <paramref name="permission"/>. The permission
    /// check is performed once per request and reused across links/items.
    /// </summary>
    public async Task AddIfGranted(
        string permission, string rel, string action, string controller,
        object? values = null, string? method = null, CancellationToken ct = default)
    {
        if (await IsGrantedAsync(permission, ct))
            Add(rel, action, controller, values, method);
    }

    private async Task<bool> IsGrantedAsync(string permission, CancellationToken ct)
    {
        if (_permissionCache.TryGetValue(permission, out var granted))
            return granted;

        granted = await _permissions.IsGrantedAsync(permission, ct);
        _permissionCache[permission] = granted;
        return granted;
    }
}
