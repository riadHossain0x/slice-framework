using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Slice.Authorization;

namespace Slice.AspNetCore.Hypermedia;

/// <summary>
/// Builds a HAL JSON document from a resource value — shared by the MVC <see cref="HalResourceFilter"/>
/// and the minimal-API <c>HalEndpointFilter</c> so both produce identical output. For a single resource it
/// adds a sibling <c>_links</c> object; for a collection it nests items under <c>_embedded</c> with
/// collection-level <c>_links</c>. Links come from the <see cref="IResourceLinkContributor{T}"/> registered
/// for the value's runtime type and are permission-filtered.
/// </summary>
public static class HalDocumentFactory
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly ConcurrentDictionary<Type, MethodInfo?> ContributeMethods = new();

    /// <summary>True when the request's <c>Accept</c> header includes <c>application/hal+json</c>.</summary>
    public static bool AcceptsHal(HttpRequest request)
    {
        foreach (var value in request.Headers.Accept)
            if (value is not null && value.Contains(Hal.MediaType, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>Serializes <paramref name="value"/> (single resource or collection) to a HAL JSON string.</summary>
    public static Task<string> BuildJsonAsync(object value, HttpContext http, CancellationToken ct)
    {
        var sp = http.RequestServices;
        var links = sp.GetRequiredService<LinkGenerator>();
        var permissions = sp.GetRequiredService<IPermissionChecker>();
        var permissionCache = new Dictionary<string, bool>(StringComparer.Ordinal);

        return value is IEnumerable enumerable and not string
            ? BuildCollectionAsync(enumerable, http, sp, links, permissions, permissionCache, ct)
            : BuildSingleAsync(value, sp, http, links, permissions, permissionCache, ct);
    }

    private static async Task<string> BuildSingleAsync(
        object resource, IServiceProvider sp, HttpContext http, LinkGenerator links,
        IPermissionChecker permissions, Dictionary<string, bool> permissionCache, CancellationToken ct)
    {
        var node = JsonSerializer.SerializeToNode(resource, resource.GetType(), Json)!.AsObject();
        var builder = await ContributeAsync(resource, sp, http, links, permissions, permissionCache, ct);

        var resourceLinks = builder?.Links;
        if (resourceLinks is null or { Count: 0 })
            resourceLinks = new Dictionary<string, HalLink> { [Hal.Self] = SelfFromRequest(http) };

        node["_links"] = ToLinksNode(resourceLinks);
        return node.ToJsonString(Json);
    }

    private static async Task<string> BuildCollectionAsync(
        IEnumerable items, HttpContext http, IServiceProvider sp, LinkGenerator links,
        IPermissionChecker permissions, Dictionary<string, bool> permissionCache, CancellationToken ct)
    {
        var array = new JsonArray();
        var embeddedRel = "items";

        foreach (var item in items)
        {
            if (item is null)
                continue;

            var itemNode = JsonSerializer.SerializeToNode(item, item.GetType(), Json)!.AsObject();
            var builder = await ContributeAsync(item, sp, http, links, permissions, permissionCache, ct);
            if (builder is not null)
            {
                embeddedRel = builder.EmbeddedRel;
                if (builder.Links.Count > 0)
                    itemNode["_links"] = ToLinksNode(builder.Links);
            }

            array.Add(itemNode);
        }

        return new JsonObject
        {
            ["_links"] = new JsonObject { [Hal.Self] = ToLinkNode(SelfFromRequest(http)) },
            ["_embedded"] = new JsonObject { [embeddedRel] = array }
        }.ToJsonString(Json);
    }

    private static async Task<LinkBuilder?> ContributeAsync(
        object resource, IServiceProvider sp, HttpContext http, LinkGenerator links,
        IPermissionChecker permissions, Dictionary<string, bool> permissionCache, CancellationToken ct)
    {
        var contributorType = typeof(IResourceLinkContributor<>).MakeGenericType(resource.GetType());
        var contributor = sp.GetService(contributorType);
        if (contributor is null)
            return null;

        var builder = new LinkBuilder(http, links, permissions, permissionCache);
        var method = ContributeMethods.GetOrAdd(contributorType, t => t.GetMethod("ContributeAsync"));
        if (method is null)
            return builder;

        await (Task)method.Invoke(contributor, [resource, builder, ct])!;
        return builder;
    }

    private static HalLink SelfFromRequest(HttpContext http) => new(http.Request.GetEncodedUrl());

    private static JsonObject ToLinksNode(IDictionary<string, HalLink> links)
    {
        var obj = new JsonObject();
        foreach (var (rel, link) in links)
            obj[rel] = ToLinkNode(link);
        return obj;
    }

    private static JsonObject ToLinkNode(HalLink link)
    {
        var node = new JsonObject { ["href"] = link.Href };
        if (link.Templated) node["templated"] = true;
        if (link.Method is not null) node["method"] = link.Method;
        if (link.Title is not null) node["title"] = link.Title;
        return node;
    }
}
