# Slice.AspNetCore.Hypermedia

> HAL hypermedia for Slice APIs — content-negotiated `_links`/`_embedded`, permission-aware, with zero impact on plain-JSON clients.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

`Slice.AspNetCore.Hypermedia` enriches resource responses with [HAL](https://stateless.group/hal_specification.html)
hypermedia — but only when the caller asks for it (`Accept: application/hal+json`). A plain
`application/json` request gets the exact same body it did before, so adding the package is
non-breaking. You declare a resource's links in an `IResourceLinkContributor<TResource>`; a global
result filter resolves the contributor for the response's runtime type, builds the links (resolving
hrefs from the route table and **hiding any link the caller is not permitted to follow**), and emits a
HAL document. Single resources get a sibling `_links` object; collections nest items under `_embedded`
with collection-level `_links`.

## Dependencies

- **Slice:** `Slice.AspNetCore` (module depends on `SliceAspNetCoreModule`), `Slice.Authorization` (permission-aware links), `Slice.Modularity`
- **Third-party:** `FrameworkReference Microsoft.AspNetCore.App`

## Module & registration

Add `SliceHypermediaModule` to your module graph, then register the link contributors in each feature
assembly:

```csharp
[DependsOn(typeof(SliceHypermediaModule))]
public sealed class CrmModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
        => context.Services.AddResourceLinkContributors(typeof(CrmModule).Assembly);
}
```

## Writing a contributor

```csharp
public sealed class LeadLinks : IResourceLinkContributor<LeadDto>
{
    public async Task ContributeAsync(LeadDto lead, LinkBuilder links, CancellationToken ct)
    {
        links.EmbeddedRel = "leads";                              // collection key under _embedded
        links.Self("Get", "GetLead", new { id = lead.Id });       // _links.self
        links.Add("list", "List", "ListLeads");                   // _links.list

        // Emitted only when the caller holds the permission (checked once per request).
        await links.AddIfGranted(
            CrmPermissions.Leads.Edit, "update", "ChangeStatus", "ChangeLeadStatus",
            new { id = lead.Id }, method: "PATCH", ct: ct);
    }
}
```

### Targeting a link three ways

| Mechanism | Methods | Use when |
|---|---|---|
| Action + controller | `Self`, `Add`, `AddIfGranted` | The endpoint is cleanly addressable by its action method and controller (the common case). |
| Route name | `SelfRoute`, `AddRoute`, `AddRouteIfGranted` | Complex/parameterized routes that opt in with `[HttpGet("…", Name = "X")]`. Resolved via `GetUriByName` — still absolute, with route values substituted. |
| Literal href | `SelfHref`, `AddHref`, `AddHrefIfGranted` (and `AddLink`) | Fully custom or external/gateway-prefixed URLs that `LinkGenerator` can't resolve. Emitted verbatim. |

```csharp
links.AddRoute("audit", "WidgetAudit", new { id = widget.Id });   // [HttpGet("…/audit", Name = "WidgetAudit")]
links.AddHref("docs", "https://docs.example/widgets");            // external link
```

A HAL request then returns:

```jsonc
GET /api/crm/leads/{id}   Accept: application/hal+json
{
  "id": "…", "fullName": "Ada Lovelace", "status": "New",
  "_links": {
    "self":   { "href": "https://…/api/crm/leads/…" },
    "list":   { "href": "https://…/api/crm/leads" },
    "update": { "href": "https://…/api/crm/leads/…/status", "method": "PATCH" }
  }
}
```

## Key types

| Type | Kind | Description |
|---|---|---|
| `HalLink` | record | A single HAL link: `Href` (required), optional `Method`/`Title`/`Templated`. |
| `Hal` | static class | Constants: `Hal.MediaType` (`application/hal+json`), `Hal.Self`. |
| `IResourceLinkContributor<TResource>` | interface | Declares the links for a resource type. |
| `LinkBuilder` | class | Collects links three ways — **action+controller** (`Self`/`Add`/`AddIfGranted`), **route name** (`SelfRoute`/`AddRoute`/`AddRouteIfGranted`, via `GetUriByName`), and **literal href** (`SelfHref`/`AddHref`/`AddHrefIfGranted`, plus `AddLink`) — with `EmbeddedRel` for collections. Resolved hrefs come from `LinkGenerator`; permission results are memoized per request. |
| `HalResourceFilter` | result filter | Content-negotiated; wraps single resources and collections into HAL. Registered globally by the module. |
| `SliceHypermediaModule` | module | `[DependsOn(SliceAspNetCoreModule, SliceAuthorizationModule)]`; registers the filter. |
| `SliceHypermediaExtensions.AddResourceLinkContributors(assembly)` | extension | Scans an assembly for contributors (mirrors `AddRequestHandlers`). |

## Minimal APIs

The same HAL output works for minimal-API endpoints via an `IEndpointFilter`. Add `.WithHal()` to an endpoint
or `.AddHal()` to a route group (e.g. inside `MapSliceEndpoints(..., g => g.AddHal())`); when the caller
negotiates `application/hal+json`, `HalEndpointFilter` unwraps the returned `Result<T>` and renders the HAL
document via the shared `HalDocumentFactory` — identical to the controller path.

## Notes

- **Non-breaking:** the filter no-ops unless `application/hal+json` is in the `Accept` header. Existing JSON clients and tests are unaffected.
- **Permission-aware:** `AddIfGranted` calls `IPermissionChecker`; a read-only user sees a resource without its write links. Each distinct permission is checked at most once per request.
- **No named routes needed:** hrefs are resolved by action + controller name against attribute routing, using the current request's scheme/host.
- **Unmapped types:** a resource with no registered contributor still gets a `self` link derived from the current request URL.
