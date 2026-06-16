# Slice.Authentication

> ASP.NET Identity + an OpenIddict OAuth2/OIDC server: users, roles, a token endpoint, claims-based permissions, and the current-user accessor.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

This module turns the framework into an authenticating web app. It hosts Guid-keyed ASP.NET Identity (`SliceUser`/`SliceRole`) together with an OpenIddict authorization server exposing `/connect/token` (password + refresh-token grants) and OpenIddict validation for the resource side. On issuance it expands a user's role-claims into `permission` claims on the access token; `ClaimsPermissionStore` then reads those claims to answer `IPermissionStore` checks, and `HttpCurrentUser` exposes the request principal as `ICurrentUser`. It replaces the Core null current-user and the configuration-backed permission store from `Slice.Authorization`.

## Dependencies

- **Slice:** `Slice.Core` (`ICurrentUser`, DI markers), `Slice.Application`, `Slice.Authorization` (`IPermissionStore`, `IPermissionDefinitionManager`), `Slice.Modularity`.
- **Third-party:** `Microsoft.AspNetCore.App`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `OpenIddict.AspNetCore`, `OpenIddict.EntityFrameworkCore`.

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

`ConfigureServices` binds `SliceAuthOptions` from the `"SliceAuth"` section, adds the HTTP context accessor, identity core (`AddIdentityCore<SliceUser>` + `AddRoles<SliceRole>` + EF stores + sign-in manager), authentication (default scheme = OpenIddict validation), authorization, the OpenIddict core/server/validation, and finally `AddSliceConventions(...)` so the claims/HTTP implementations win over the Core/config defaults. `OnApplicationInitializationAsync` runs `SliceAuthDataSeeder.SeedAsync` in a fresh scope.

## Key types

| Type | Kind | Description |
|---|---|---|
| `SliceUser` | `sealed class : IdentityUser<Guid>` | Application user. |
| `SliceRole` | `sealed class : IdentityRole<Guid>` | Application role; carries `permission` role-claims. |
| `SliceAuthDbContext` | `sealed class : IdentityDbContext<SliceUser, SliceRole, Guid>` | Identity + OpenIddict store (`builder.UseOpenIddict()`). |
| `SliceAuthOptions` | `sealed class` | Bound from `"SliceAuth"`. See defaults below. |
| `SliceClaims` | `static class` | `Permission = "permission"`. |
| `ClaimsPermissionStore` | `sealed class`, `IPermissionStore`, `ISingletonDependency` | Granted when the principal carries a matching `permission` claim. |
| `HttpCurrentUser` | `sealed class`, `ICurrentUser`, `ISingletonDependency` | Reads `sub`/name/role claims from the request principal. |
| `ConnectController` | `sealed class : ControllerBase` | `POST ~/connect/token` (`Exchange()`); password + refresh-token grants. |
| `SliceAuthDataSeeder` | `static class` | `SeedAsync(IServiceProvider)`; ensures schema, admin role + demo admin. |
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

`ConnectController.Exchange()` validates credentials, then `CreatePrincipalAsync` builds a principal with `sub`/name/email claims, the user's role claims, and a deduplicated set of `permission` claims gathered from each role's `permission` role-claims. All claims are sent only to the access token (`Destinations.AccessToken`), which is issued as a plain JWT.

Read the current user inside a handler:

```csharp
public sealed class Handler(ICurrentUser user) // HttpCurrentUser
{
    // user.IsAuthenticated, user.Id (Guid?), user.UserName, user.Roles (string[])
}
```

## Notes

- **`SliceAuthOptions` defaults:** `ConnectionString = "Data Source=auth.db"`, `SeedDemoAdmin = true`, `AdminEmail = "admin@slice"`, `AdminPassword = "Admin123!"`, `AdminRole = "admin"`.
- **Demo admin / seeding:** when `SeedDemoAdmin` is true, `SliceAuthDataSeeder` ensures the schema (`EnsureCreatedAsync`), creates the `admin` role granted **every declared permission** as `permission` role-claims, and creates `admin@slice` / `Admin123!` in that role. Change these for anything beyond local dev.
- **OpenIddict server config:** token endpoint `connect/token`; password + refresh-token flows; `AcceptAnonymousClients()` (first-party public client — no client secret required); scopes `"api"` and `"offline_access"` registered; access-token encryption disabled (plain JWT); ASP.NET Core passthrough with transport-security requirement disabled (HTTP-friendly dev).
- **Ephemeral keys:** signing and encryption keys are in-memory (`AddEphemeralSigningKey` / `AddEphemeralEncryptionKey`) — they rotate on every restart, invalidating prior tokens. Register real X.509 certificates for production.
- **`HttpCurrentUser`** is a singleton (depends only on the singleton `IHttpContextAccessor`), so the auditing interceptor can keep using it; `Id` reads OpenIddict `sub` (falling back to `NameIdentifier`); `Roles` merges OpenIddict and `ClaimTypes.Role` claims.
- This module's claims store reflects the token's contents — changing a user's roles/permissions only takes effect after a new token is issued. Use `Slice.Management` for DB-backed grants that apply immediately.
