# Hypermedia & HTTP caching

Two optional, opt-in web libraries add production-grade REST affordances on top of the
[`Result<T>` → HTTP](web-and-results.md) pipeline — without changing any controller or breaking
existing JSON clients:

- **`Slice.AspNetCore.Hypermedia`** — HAL hypermedia (`_links`/`_embedded`), content-negotiated and
  permission-aware.
- **`Slice.AspNetCore.ConditionalRequests`** — `ETag` + `If-None-Match` → `304`, and `If-Match` → `412`
  optimistic concurrency.

Both plug into the existing wrap point (a global result filter / a middleware), reuse what the framework
already has — `LinkGenerator`, `IPermissionChecker`, and the `ConcurrencyStamp` the auditing interceptor
already maintains — and require **no changes to `SliceController` or `Result<T>`**.

---

## HATEOAS with HAL

### Why it's non-breaking

The HAL result filter only acts when the client sends `Accept: application/hal+json`. A plain
`application/json` request returns the identical body it always did. So you can add hypermedia to an
existing API and adopt it client-by-client.

### Declaring links

Implement `IResourceLinkContributor<TResource>` per DTO and register the contributors per assembly with
`AddResourceLinkContributors(assembly)`. Links resolve their hrefs from the route table (attribute
routing → resolved by action + controller name), and state-changing links are emitted **only when the
caller is permitted to follow them**:

```csharp
public sealed class LeadLinks : IResourceLinkContributor<LeadDto>
{
    public async Task ContributeAsync(LeadDto lead, LinkBuilder links, CancellationToken ct)
    {
        links.EmbeddedRel = "leads";
        links.Self("Get", "GetLead", new { id = lead.Id });
        links.Add("list", "List", "ListLeads");
        await links.AddIfGranted(
            CrmPermissions.Leads.Edit, "update", "ChangeStatus", "ChangeLeadStatus",
            new { id = lead.Id }, method: "PATCH", ct: ct);
    }
}
```

A link can be targeted three ways — pick whichever addresses the endpoint:

- **action + controller** — `Self`/`Add`/`AddIfGranted` (the common case, resolved via `GetUriByAction`).
- **route name** — `SelfRoute`/`AddRoute`/`AddRouteIfGranted` for complex/parameterized routes that opt in
  with `[HttpGet("…", Name = "X")]` (resolved via `GetUriByName`; still absolute, with route values filled).
- **literal href** — `SelfHref`/`AddHref`/`AddHrefIfGranted` for fully custom or external/gateway-prefixed
  URLs `LinkGenerator` can't resolve (emitted verbatim).

```csharp
links.AddRoute("audit", "LeadAudit", new { id = lead.Id });   // [HttpGet("…/audit", Name = "LeadAudit")]
links.AddHref("docs", "https://docs.example/leads");          // external link
```

### Single vs collection

- A **single** resource gets a sibling `_links` object next to its own properties.
- A **collection** nests its items under `_embedded.<rel>` (the `EmbeddedRel`, default `"items"`), each
  item carrying its own `_links`, plus a collection-level `_links.self`.

```jsonc
// GET /api/crm/leads   Accept: application/hal+json
{
  "_links": { "self": { "href": "https://…/api/crm/leads" } },
  "_embedded": {
    "leads": [
      { "id": "…", "fullName": "Ada Lovelace",
        "_links": { "self": { "href": "https://…/api/crm/leads/…" } } }
    ]
  }
}
```

A read-only caller (only `Crm.Leads.View`) sees each lead **without** the `update`/`export` links — the
client can drive its UI off link presence instead of re-deriving permissions.

---

## Conditional requests (ETag / 304 / 412)

Register the module and add the middleware **after** the exception handler:

```csharp
app.UseSliceExceptionHandling();
app.UseSliceConditionalRequests();   // inside the exception handler
app.MapControllers();
```

### Reads → ETag and 304

Expose a cheap version on the DTO and every GET gets a strong `ETag` for free (no body hashing):

```csharp
public sealed record LeadDto(Guid Id, /* … */ string ConcurrencyStamp) : IHasResourceVersion
{
    [JsonIgnore] public string ResourceVersion => ConcurrencyStamp;
}
```

```
GET /api/crm/leads/{id}                         → 200, ETag: "ab12…"
GET /api/crm/leads/{id}  If-None-Match: "ab12…" → 304 Not Modified (no body)
```

DTOs that don't implement `IHasResourceVersion` still get an ETag — the middleware hashes the response
body (capped by `ConditionalRequestOptions.MaxBodyBytes`). `Vary: Accept` is emitted so a HAL and a
plain-JSON representation cache separately.

### Writes → If-Match and 412

Carry the client's `If-Match` into the command and pin it onto EF Core's concurrency token. If another
writer moved the row on, `SaveChanges` raises `DbUpdateConcurrencyException`, mapped to **412**:

```csharp
// controller
=> SendAsync(new ChangeLeadStatusCommand(id, body.Status, HttpContext.GetIfMatch()), ct);

// handler
lead.ChangeStatus(command.Status);
db.Entry(lead).UseIfMatch(command.IfMatch);   // WHERE ConcurrencyStamp = @ifMatch
```

```
PATCH /api/crm/leads/{id}/status  If-Match: "stale"  → 412 Precondition Failed
PATCH /api/crm/leads/{id}/status  If-Match: "ab12…"  → 200 (ConcurrencyStamp rolls)
```

Because the middleware sits *inside* `UseSliceExceptionHandling`, it maps the concurrency exception to
412 before the generic 500 handler runs. A handler can also throw `PreconditionFailedException` to force
a 412 explicitly.

---

## Trade-offs

- The content-hash ETag path buffers the response (reads only); keep `MaxBodyBytes` sensible and prefer a
  version ETag, which skips hashing.
- HAL adds one `IPermissionChecker` call per gated link (memoized per request).
- Hypermedia targets resource/collection **GET**s; the value boundary is `ObjectResult.Value`, so a
  `Result<T>` is unwrapped to its value before the filter runs.

See the package READMEs for the full API: [`Slice.AspNetCore.Hypermedia`](../src/Slice.AspNetCore.Hypermedia/README.md),
[`Slice.AspNetCore.ConditionalRequests`](../src/Slice.AspNetCore.ConditionalRequests/README.md).
