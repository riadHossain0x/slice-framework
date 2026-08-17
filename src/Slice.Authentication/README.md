# Slice.Authentication

> ASP.NET Identity + an OpenIddict OAuth2/OIDC server: users, roles, a token endpoint, claims-based permissions, and the current-user accessor.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

This module turns the framework into an authenticating web app. It hosts Guid-keyed ASP.NET Identity (`SliceUser`/`SliceRole`) together with an OpenIddict authorization server exposing `/connect/token` (password + refresh-token grants) and OpenIddict validation for the resource side. On issuance it expands a user's role-claims into `permission`/`data_scope` claims on the access token; `ClaimsPermissionStore` then reads the permission claims (honoring a `permission_deny` override) to answer `IPermissionStore` checks, and `HttpCurrentUser` exposes the request principal as `ICurrentUser`. It replaces the Core null current-user and the configuration-backed permission store from `Slice.Authorization`.

`SliceUser`/`SliceRole` are also tenant-scoped (`Guid? TenantId`, `IMultiTenant`) — a null `TenantId` is a platform/host identity, a non-null one is in-tenant staff, and the same username/email can exist once per tenant. See **[docs/multitenancy.md](../../docs/multitenancy.md#tenant-scoped-identity)** for the full design, including two gotchas worth reading before touching role assignment or login code: the NULL-uniqueness index gap, and why role names must be resolved via `ITenantRoleAssigner` rather than the stock by-name Identity APIs.

## Dependencies

- **Slice:** `Slice.Core` (`ICurrentUser`, DI markers), `Slice.Application`, `Slice.Authorization` (`IPermissionStore`, `IPermissionDefinitionManager`), `Slice.Modularity`, `Slice.Domain` (`IHasExtraProperties`, `IMultiTenant`/`MultiTenancyClaims` — transitive via `Slice.Application`).
- **Third-party:** `Microsoft.AspNetCore.App`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`, `OpenIddict.AspNetCore`, `OpenIddict.EntityFrameworkCore`.

## Module & registration

`SliceAuthenticationModule` is a `SliceModule` with `[DependsOn(typeof(SliceAuthorizationModule))]`. Because the framework stays provider-agnostic, the host registers the EF store via `AddSliceAuthStore(...)` (the module itself does *not* register the `DbContext`), and adds middleware via `UseSliceAuthentication()`.

```csharp
[DependsOn(typeof(SliceAuthenticationModule))]
public sealed class MyAppModule : SliceModule { }

// Host wiring:
services.AddSliceAuthStore(b => b.UseSqlite("Data Source=auth.db"));

// Host pipeline (before MapControllers):
app.UseSliceAuthentication(); // UseAuthentication() + UseAuthorization()
```

`ConfigureServices` binds `SliceAuthOptions` from the `"SliceAuth"` section, adds the HTTP context accessor, identity core (`AddIdentityCore<SliceUser>` + `AddRoles<SliceRole>` + EF stores + sign-in manager, with `RequireUniqueEmail = false` since uniqueness is now per-tenant), then **replaces** the default `IUserValidator<SliceUser>`/`IRoleValidator<SliceRole>` with `TenantScopedUserValidator`/`TenantScopedRoleValidator` (`RemoveAll<T>()` first — `AddIdentityCore`'s defaults are `TryAddScoped`, which doesn't stop a second validator from also running) and the role-aware `IUserClaimsPrincipalFactory<SliceUser>` that `AddRoles<SliceRole>()` installed with `TenantSafeUserClaimsPrincipalFactory`, registers Data Protection key persistence into the same store, then authentication (default scheme = OpenIddict validation), authorization, the OpenIddict core/server/validation, and finally `AddSliceConventions(...)` so the claims/HTTP implementations win over the Core/config defaults. `OnApplicationInitializationAsync` runs `SliceAuthDataSeeder.SeedAsync` in a fresh scope.

## Key types

| Type | Kind | Description |
|---|---|---|
| `SliceUser` | `class : IdentityUser<Guid>, IMultiTenant` | Application user; `Guid? TenantId` (null = platform tier). |
| `SliceRole` | `class : IdentityRole<Guid>, IMultiTenant` | Application role; `Guid? TenantId`; carries `permission`/`data_scope` role-claims. |
| `SliceAuthDbContext` | `class : IdentityDbContext<SliceUser, SliceRole, Guid>, IDataProtectionKeyContext` | Identity + OpenIddict + Data Protection key store; per-tenant uniqueness indexes on both `NormalizedUserName` and `NormalizedEmail` (see docs). Not a `SliceDbContext`, so `IMultiTenant` here is a marker only — no global query filter, no automatic `TenantId` stamping. |
| `SliceAuthOptions` | `sealed class` | Bound from `"SliceAuth"`. See defaults below. |
| `SliceClaims` | `static class` | `Permission = "permission"`, `PermissionDeny = "permission_deny"`, `DataScope = "data_scope"`, `TenantId = "tenant_id"` (shared with `Slice.MultiTenancy.TenantConstants.Claim`). |
| `ClaimsPermissionStore` | `sealed class`, `IPermissionStore`, `ISingletonDependency` | Granted when the principal carries a matching `permission` claim and no matching `permission_deny` claim. |
| `ITenantRoleAssigner` / `TenantRoleAssigner` | interface / `sealed class`, `IScopedDependency` | Tenant-safe role resolution/assignment — use instead of `RoleManager.FindByNameAsync`/`UserManager.AddToRoleAsync` and friends once roles are tenant-scoped. `FindRoleAsync`/`FindRolesAsync`/`GetRolesOfUserAsync`/`AddToRoleAsync`/`AddToRolesAsync`/`RemoveFromRoleAsync`/`RemoveFromRolesAsync`/`IsInRoleAsync`/`GetUsersInRoleAsync`/`CountUsersInRoleAsync`. |
| `TenantSafeUserClaimsPrincipalFactory` | `class : UserClaimsPrincipalFactory<SliceUser>` | Registered by the module in place of the role-aware default, whose unfiltered `FindByNameAsync` can merge another tenant's role claims into a signed-in principal. Produces the same claim set as an access token. |
| `LoginCandidates` | `sealed record` | Result of `ConnectController.ResolveUserAsync`: `Matches` plus `ExceededCandidateLimit`. |
| `TenantScopedUserValidator` / `TenantScopedRoleValidator` | `sealed class`, `IUserValidator<SliceUser>` / `IRoleValidator<SliceRole>` | Enforce username, **email** and role-name uniqueness scoped to `(TenantId, Normalized*)` instead of globally. |
| `HttpCurrentUser` | `sealed class`, `ICurrentUser`, `ISingletonDependency` | Reads `sub`/name/role claims from the request principal. |
| `ConnectController` | `class : ControllerBase` | `POST ~/connect/token` (`Exchange()`); tenant-aware password + refresh-token grants. Subclassable: `ResolveUserAsync`, `RejectAmbiguousAsync`, `CreatePrincipalAsync` are `protected virtual`. |
| `SliceAuthDataSeeder` | `static class` | `SeedAsync(IServiceProvider)`; ensures schema, platform-tier admin role + demo admin. |
| `SliceAuthenticationModule` | `sealed class : SliceModule` | Wires Identity + OpenIddict. |
| `SliceAuthStoreRegistration` | `static class` | `AddSliceAuthStore(this IServiceCollection, Action<DbContextOptionsBuilder>)`. |
| `AuthApplicationBuilderExtensions` | `static class` | `UseSliceAuthentication(this IApplicationBuilder)`. |

## Usage

Obtain a token (password grant):

```http
POST /connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=password&username=admin@slice&password=Admin123!&scope=api offline_access
```

`ConnectController.Exchange()` resolves every `SliceUser` row matching the submitted username/email and checks the password against each (since usernames are only unique per tenant, not globally) — zero or one match behaves as before; more than one match (the same email+password valid in more than one tenant) is rejected asking for a `tenant_id` form parameter to disambiguate. Because each check is a full password hash, no more than `MaxLoginCandidates` rows are ever checked in one request; past that the request is rejected without hashing anything, using the generic invalid-credentials message (a specific one would let an unauthenticated caller measure how widely an address is used across tenants). Supplying `tenant_id` narrows the query before the cap applies, so such a caller is never capped.

`CreatePrincipalAsync` then builds a principal with `sub`/name/email claims, a `tenant_id` claim when the resolved user has one, the user's role claims, and deduplicated `permission`/`permission_deny`/`data_scope` claims. Those come from the roles the user is actually **linked** to (`ITenantRoleAssigner.GetRolesOfUserAsync`), not from re-resolving role names against the user's tenant — so a role deliberately granted from outside that tenant (a platform-tier support role on in-tenant staff, say) keeps its permissions instead of silently contributing only its name. All claims are sent only to the access token (`Destinations.AccessToken`), which is issued as a plain JWT.

Read the current user inside a handler:

```csharp
public sealed class Handler(ICurrentUser user) // HttpCurrentUser
{
    // user.IsAuthenticated, user.Id (Guid?), user.UserName, user.Roles (string[])
}
```

## Notes

- **`SliceAuthOptions` defaults:** `ConnectionString = "Data Source=auth.db"`, `SeedDemoAdmin = true`, `AdminEmail = "admin@slice"`, `AdminPassword = "Admin123!"`, `AdminRole = "admin"`, `MaxLoginCandidates = 10`.
- **Demo admin / seeding:** when `SeedDemoAdmin` is true, `SliceAuthDataSeeder` ensures the schema (`EnsureCreatedAsync`), creates the platform-tier (`TenantId == null`) `admin` role granted **every declared permission** as `permission` role-claims, and creates `admin@slice` / `Admin123!` in that role. Change these for anything beyond local dev.
- **OpenIddict server config:** token endpoint `connect/token`; password + refresh-token flows; `AcceptAnonymousClients()` (first-party public client — no client secret required); scopes `"api"` and `"offline_access"` registered; access-token encryption disabled (plain JWT); ASP.NET Core passthrough with transport-security requirement disabled (HTTP-friendly dev).
- **Ephemeral keys:** signing and encryption keys are in-memory (`AddEphemeralSigningKey` / `AddEphemeralEncryptionKey`) — they rotate on every restart, invalidating prior tokens. Register real X.509 certificates for production.
- **Data Protection keys live in the same store.** `SliceAuthDbContext` implements `IDataProtectionKeyContext` (`DataProtectionKeys` table) and the module calls `AddDataProtection().PersistKeysToDbContext<SliceAuthDbContext>()`, so auth cookies and other protected payloads survive restarts and stay valid across every replica rather than each one generating its own keys into a local folder. Call `AddDataProtection()` again after the module to layer on your own configuration.
- **Index filters are provider-specific SQL.** The per-tenant unique indexes use partial-index `WHERE` clauses, quoted for the host's provider (`TenantIndexFilters`). PostgreSQL, SQLite and SQL Server get filtered indexes; providers without them (MySQL/MariaDB, Oracle) fall back to the unfiltered composite index, which leaves **platform-tier** (`TenantId IS NULL`) uniqueness enforced by the validator alone rather than by the database.
- **`data_scope` is a reserved extension point.** The claim is issued from role claims and carried on the token, but nothing in the framework consumes it yet — wire your own filtering on top of it.
- **`HttpCurrentUser`** is a singleton (depends only on the singleton `IHttpContextAccessor`), so the auditing interceptor can keep using it; `Id` reads OpenIddict `sub` (falling back to `NameIdentifier`); `Roles` merges OpenIddict and `ClaimTypes.Role` claims.
- This module's claims store reflects the token's contents — changing a user's roles/permissions only takes effect after a new token is issued. Use `Slice.Management` for DB-backed grants that apply immediately.
- **Multi-tenant identity gotchas** (index design, role-name ambiguity, login disambiguation, migrating an existing deployment) are documented in depth in [docs/multitenancy.md](../../docs/multitenancy.md#tenant-scoped-identity), not repeated here.
