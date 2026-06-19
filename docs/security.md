# Security: authentication, authorization & management

Slice's security stack has three packages that build on each other:

- **`Slice.Authentication`** — *who are you?* An OpenIddict OAuth2/OIDC server + ASP.NET Identity that
  issues and validates JWTs and populates `ICurrentUser`.
- **`Slice.Authorization`** — *what may you do?* A permission system (definitions, checker, store) and
  the `[SlicePermission]` pipeline behavior.
- **`Slice.Management`** — *administering it at runtime.* DB-backed permission grants, tenants, and
  setting/feature values, plus admin controllers. DB grants make permission changes take effect
  **without re-issuing tokens**.

---

## Authentication (`Slice.Authentication`)

### What it sets up

`SliceAuthenticationModule` wires:

- **ASP.NET Identity** with Guid keys: `SliceUser : IdentityUser<Guid>`, `SliceRole : IdentityRole<Guid>`,
  stored in `SliceAuthDbContext` (Identity + OpenIddict tables).
- **OpenIddict server** at `~/connect/token` (`ConnectController`) supporting the **password** and
  **refresh_token** grants, issuing plain **JWT** access tokens. It registers the `api` and
  `offline_access` scopes, calls `AcceptAnonymousClients()` (no `client_id` needed for first-party
  flows), and uses **ephemeral** signing/encryption keys in development.
- **OpenIddict validation** for the resource side (same app is auth server + API).
- `HttpCurrentUser` as `ICurrentUser`, reading the `sub` (OpenIddict `Subject`) or
  `ClaimTypes.NameIdentifier` for the id, and the `name`/role/`permission` claims.
- `ClaimsPermissionStore` as the default `IPermissionStore` — permissions are read from `permission`
  claims embedded in the token (no per-request DB hit).

### Registration & middleware

```csharp
// in your module
services.AddSliceAuthStore(o => o.UseSqlite(config.GetConnectionString("Auth") ?? "Data Source=auth.db"));

// in Program.cs (after routing, before MapControllers)
app.UseSliceAuthentication();   // UseAuthentication() + UseAuthorization()
```

### `SliceAuthOptions`

| Property | Default | Meaning |
|---|---|---|
| `ConnectionString` | `Data Source=auth.db` | identity/OpenIddict store |
| `SeedDemoAdmin` | `true` | seed a demo admin on startup |
| `AdminEmail` | `admin@slice` | demo admin login |
| `AdminPassword` | `Admin123!` | demo admin password |
| `AdminRole` | `admin` | demo admin role (granted every declared permission) |

On startup `SliceAuthDataSeeder` ensures the schema, creates the admin role with a `permission`
role-claim for every declared permission, and creates the admin user in that role.

### Getting a token

```bash
curl -X POST http://localhost:5273/connect/token \
  --data-urlencode grant_type=password \
  --data-urlencode username=admin@slice \
  --data-urlencode password=Admin123! \
  --data-urlencode 'scope=api offline_access'
```

At token issuance, the user's permissions (aggregated from their roles' `permission` claims) are
embedded as `permission` claims in the access token, plus the `tenant_id` claim if applicable.

### Production hardening

- Replace ephemeral keys with real signing/encryption certificates.
- Restrict/replace `AcceptAnonymousClients()` with registered clients and PKCE/authorization-code where
  appropriate (`password` is included for first-party parity).
- Turn off `SeedDemoAdmin` and seed real principals.

---

## Authorization (`Slice.Authorization`)

> For a runnable, step-by-step **define → require → check → assign** guide (with a `403 → grant → 200`
> flow), see the [Permissions walkthrough](permissions.md).

### Defining permissions

```csharp
public static class CrmPermissions
{
    public const string GroupName = "Crm";
    public static class Leads
    {
        public const string View   = GroupName + ".Leads.View";
        public const string Create = GroupName + ".Leads.Create";
    }
}

public sealed class CrmPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup(CrmPermissions.GroupName, "CRM");
        var view  = group.AddPermission(CrmPermissions.Leads.View, "View leads");
        view.AddChild(CrmPermissions.Leads.Create, "Create leads");   // create implies view in the UI tree
    }
}
```

`PermissionDefinitionProvider` subclasses are discovered automatically (the convention registrar
registers them against the abstract base). `IPermissionDefinitionManager` aggregates all definitions.

A group (or individual permission) can be tied to a **feature** so a disabled module's permissions don't
surface for a tenant that lacks it:

```csharp
context.AddGroup("Sales").RequireFeature("Sales").AddPermission("Sales.Orders.Create", "Create orders");
```

Permissions inherit their group's `RequiredFeature` (exposed on `IPermissionDefinitionManager`). This is
**metadata for visibility/filtering** — the [`GET /api/app-config`](../src/Slice.AspNetCore.AppConfig/README.md)
endpoint omits permissions (and menu items) whose feature is disabled for the current tenant. Runtime
**enforcement** is still `[RequiresFeature]` / the module-level gate (`services.RequireFeature<TModule>(...)`)
— see [cross-cutting services → Features](cross-cutting-services.md#features).

### Enforcing a permission

Annotate the command/query; `AuthorizationBehavior` (pipeline order 300) enforces it before the
handler runs:

```csharp
[SlicePermission(CrmPermissions.Leads.Create)]
public sealed record CreateLeadCommand(/* … */) : ICommand<Result<Guid>>;
```

On denial the behavior returns `Error.Forbidden("Authorization:Forbidden", …)` as a failed `Result`,
which the controller maps to **403**. Put `[Authorize]` on the controller too so an authenticated
principal exists before the permission is checked.

### The permission store seam

```csharp
public interface IPermissionChecker { Task<bool> IsGrantedAsync(string permission, CancellationToken ct = default); }
public interface IPermissionStore   { Task<bool> IsGrantedAsync(string permission, CancellationToken ct = default); }
```

The active `IPermissionStore` decides the answer. There are three, each superseding the previous as
you add the module that registers it (last-wins, dependency-ordered):

| Store | Package | Source |
|---|---|---|
| `ConfigurationPermissionStore` | `Slice.Authorization` | `Authorization:GrantedPermissions` config + `GrantedByDefault` |
| `ClaimsPermissionStore` | `Slice.Authentication` | `permission` claims on the token |
| `PermissionManagementStore` | `Slice.Management` | DB grants (per user or role) |

---

## Management (`Slice.Management`)

`Slice.Management` consolidates ABP's five `*Management` modules into one package over a shared
`SliceManagementDbContext` with four tables:

| Entity | Table | Holds |
|---|---|---|
| `PermissionGrant` | `SlicePermissionGrants` | `(Name, ProviderName, ProviderKey)` |
| `TenantRecord` | `SliceTenants` | `(Id, Name)` |
| `SettingValueRecord` | `SliceSettingValues` | `(Name, Value, ProviderName, ProviderKey)` |
| `FeatureValueRecord` | `SliceFeatureValues` | `(Name, Value, ProviderName, ProviderKey)` |

Provider keys use `PermissionProviders.Role = "R"` and `PermissionProviders.User = "U"`.

### DB-backed permissions

`PermissionManagementStore` answers `IsGrantedAsync` by checking the `SlicePermissionGrants` table for
a grant to the current user (`"U"`, the user id) **or** any of the user's roles (`"R"`). Because it
hits the database on each check, **revoking a permission takes effect on the very next request — no
new token required.** On startup the module seeds every declared permission to the `admin` role.

```csharp
// in your module
services.AddSliceManagementStore(o => o.UseSqlite(config.GetConnectionString("Management") ?? "Data Source=mgmt.db"));
```

### Admin controllers

| Route | Operations |
|---|---|
| `api/management/permissions` | `GET` granted, `POST grant`, `POST revoke` (by `providerName`/`providerKey`/`permission`) |
| `api/management/tenants` | `GET` list, `POST` create |
| `api/management/identity` | `POST roles`, `POST users` (optionally assigning a role) |

```bash
# revoke a permission from a role — effective immediately, same token
curl -X POST http://localhost:5273/api/management/permissions/revoke \
  -H "Authorization: Bearer $ADMIN" -H "Content-Type: application/json" \
  -d '{"providerName":"R","providerKey":"admin","permission":"Crm.Leads.Create"}'
```

> The management controllers carry `[Authorize]` (authenticated). Add `[SlicePermission(...)]` or your
> own policy if you want to gate administration behind a specific permission.

### Settings & features at runtime

`ManagementSettingValueProvider` (high-priority, order −10) reads setting values from the DB
(user → tenant → global), and `ManagementFeatureStore` reads feature values from the DB (tenant →
global) before falling back to configuration. This makes settings/features editable at runtime rather
than only via `appsettings`. See [Cross-cutting services](cross-cutting-services.md).

---

## End-to-end: the dynamic-permission flow

This is the behaviour the sample verifies:

1. No token → **401**.
2. Admin token (DB grant seeded) → create lead **200**.
3. `POST /api/management/permissions/revoke` `Crm.Leads.Create` from role `admin` → next create **403**
   *(same token — the DB store, not the claims store, decides)*.
4. Re-grant → **200**.
5. Create role `agent` + user `jo` granted only `Crm.Leads.View` → list **200** / create **403**.
