# Permissions — a walkthrough

A practical, runnable guide to **define → require → check → assign**. For the conceptual breakdown of
the stores and how they layer, see [Security](security.md#authorization-sliceauthorization). This page
follows one real permission (`Crm.Leads.Export`) end-to-end in the `Slice.Sample.Crm` sample.

## 1. Define the permission

Declare permissions in a `PermissionDefinitionProvider` (auto-discovered; aggregated by
`IPermissionDefinitionManager`). `samples/Slice.Sample.Crm/Permissions/CrmPermissions.cs`:

```csharp
public static class CrmPermissions
{
    public const string GroupName = "Crm";
    public static class Leads
    {
        public const string View   = GroupName + ".Leads.View";
        public const string Create = GroupName + ".Leads.Create";
        public const string Export = GroupName + ".Leads.Export";   // ← the one we gate below
    }
}

public sealed class CrmPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup(CrmPermissions.GroupName, "CRM");
        var view  = group.AddPermission(CrmPermissions.Leads.View, "View leads");
        view.AddChild(CrmPermissions.Leads.Create, "Create leads");
        view.AddChild(CrmPermissions.Leads.Export, "Export leads");
    }
}
```

The hierarchy (children under `view`) is for grouping/UI only — checks are by **exact name**.

## 2. Require it on a use case

Put `[SlicePermission(...)]` on the **command/query** (not the controller), so it's enforced for every
caller — HTTP and background. `samples/Slice.Sample.Crm/Features/ExportLeads/ExportLeads.cs`:

```csharp
[SlicePermission(CrmPermissions.Leads.Export)]
public sealed record ExportLeadsQuery : IQuery<Result<string>>;

public sealed class ExportLeadsHandler(ILeadRepository repository)
    : IQueryHandler<ExportLeadsQuery, Result<string>>
{
    public async Task<Result<string>> HandleAsync(ExportLeadsQuery query, CancellationToken ct) { /* build CSV */ }
}

[Authorize]                                  // authentication: a principal must exist
[Route("api/crm/leads/export")]
public sealed class ExportLeadsController : SliceController
{
    [HttpGet] public Task<IActionResult> Export(CancellationToken ct) => SendAsync(new ExportLeadsQuery(), ct);
}
```

## 3. How the check runs

`AuthorizationBehavior` runs in the mediator pipeline at order **300**, *before* the handler. It reads
the `[SlicePermission]` attributes and calls `IPermissionChecker.IsGrantedAsync(name)` →
`IPermissionStore.IsGrantedAsync(name)`. On the first missing permission it short-circuits to
`Error.Forbidden` → **HTTP 403**; the handler never runs and the unit of work never commits.

```
SendAsync → LoggingBehavior(100) → MultiTenancy(200) → Authorization(300) → … → handler
                                                          │ IPermissionChecker → IPermissionStore
                                                          └─ not granted ⇒ 403, stop
```

The active **`IPermissionStore`** is the source of truth (last-registered wins, dependency-ordered):
`ConfigurationPermissionStore` (config) → `ClaimsPermissionStore` (token claims) →
`PermissionManagementStore` (DB grants). The CRM sample uses `Slice.Management`, so **DB grants** decide.

## 4. Assign — two models

**A. Role claims baked into the token** (`Slice.Authentication`). Permissions are attached to a role as
`permission` role-claims and embedded into the JWT at `/connect/token`. Effective on the **next token**
(re-login). Used when running on the claims store.

**B. DB grants** (`Slice.Management`, recommended). Grants live in `SlicePermissionGrants` keyed by
provider `"R"`+role or `"U"`+user; managed via `api/management/permissions`. Effective on the **next
request with the same token** — no reissue. The module seeds every declared permission to the `admin`
role at startup, which is why admin can already export.

## 5. Run it (the verified flow)

```bash
dotnet run --project samples/Slice.Sample.Crm        # admin@slice / Admin123!
B=http://localhost:5273
tok(){ curl -s -X POST $B/connect/token --data-urlencode grant_type=password \
  --data-urlencode "username=$1" --data-urlencode "password=$2" --data-urlencode scope=api \
  | python3 -c "import sys,json;print(json.load(sys.stdin)['access_token'])"; }

ADMIN=$(tok admin@slice 'Admin123!')

# admin is seeded with every permission → 200
curl -o /dev/null -w "%{http_code}\n" $B/api/crm/leads/export -H "Authorization: Bearer $ADMIN"   # 200

# create a viewer role + a user in it
curl -X POST $B/api/management/identity/roles -H "Authorization: Bearer $ADMIN" \
  -H "Content-Type: application/json" -d '{"name":"viewer"}'
curl -X POST $B/api/management/identity/users -H "Authorization: Bearer $ADMIN" \
  -H "Content-Type: application/json" -d '{"email":"vw@x.com","password":"Passw0rd!","role":"viewer"}'
VW=$(tok vw@x.com 'Passw0rd!')

# viewer has no Export grant → 403
curl -o /dev/null -w "%{http_code}\n" $B/api/crm/leads/export -H "Authorization: Bearer $VW"      # 403

# grant Crm.Leads.Export to the viewer role (DB grant)
curl -X POST $B/api/management/permissions/grant -H "Authorization: Bearer $ADMIN" \
  -H "Content-Type: application/json" \
  -d '{"providerName":"R","providerKey":"viewer","permission":"Crm.Leads.Export"}'

# SAME viewer token now works — no re-login → 200
curl -o /dev/null -w "%{http_code}\n" $B/api/crm/leads/export -H "Authorization: Bearer $VW"      # 200
```

`403 → grant → 200` on the **same token** is the DB-store property: permission changes take effect
immediately. (On the claims store, the viewer would need to re-login to pick up a new role claim.)

## Notes

- Gate the **request type**, not the controller, so non-HTTP callers (jobs, other modules) are covered too.
- `ICurrentUser` supplies `Id` + `Roles` (from the token's `sub`/role claims) to the DB store; the
  principal's `permission` claims feed the claims store.
- A permission that is **not** `grantedByDefault` (like `Export`) is the clean way to demonstrate denial,
  since `admin` is seeded with everything.
- See also: [Security](security.md), [CQRS & the mediator pipeline](cqrs-and-mediator.md#deterministic-ordering).
