# Multi-tenancy

Slice supports **multi-tenancy** at two levels of isolation, both driven by the same ambient
`ICurrentTenant`:

- **Row-level isolation** (shared database) — a global query filter restricts `IMultiTenant` entities
  to the current tenant. This is on by default for any entity that implements `IMultiTenant`.
- **Database-per-tenant** (isolated database) — the `DbContext` connection string is resolved per
  request from the current tenant.

Packages: `Slice.MultiTenancy` (resolution, ambient, middleware), `Slice.EntityFrameworkCore`
(the filters + the per-tenant connection plumbing), `Slice.Authentication` (tenant-scoped
`SliceUser`/`SliceRole` — the identity layer that issues the `tenant_id` claim in the first place; see
"Tenant-scoped identity" below).

---

## The ambient tenant

```csharp
public interface ICurrentTenant
{
    bool IsAvailable { get; }
    Guid? Id { get; }
    string? Name { get; }
    IDisposable Change(Guid? tenantId, string? name = null);
}
```

`CurrentTenant` stores the value in an `AsyncLocal`, so it flows through async calls and DI scopes.
`Change(...)` pushes a tenant for the duration of a `using` block — used by the middleware (per
request), by background jobs, and in tests:

```csharp
using (currentTenant.Change(tenantId))
{
    // everything here — queries, the DbContext connection, audit stamping — sees this tenant
}
```

The default registration in `Slice.Core` is `NullCurrentTenant` (always host/no tenant); referencing
`Slice.MultiTenancy` replaces it with the real `CurrentTenant`.

---

## Tenant-scoped identity

`Slice.Authentication`'s `SliceUser`/`SliceRole` are tenant-scoped: both carry a nullable `TenantId`
(`IMultiTenant`). A **null** `TenantId` identifies a platform/host account — one that manages tenants
themselves, not any single tenant's data. A **non-null** `TenantId` identifies in-tenant staff, scoped
to that one tenant. The same username/email can exist once per tenant — Identity's own
`RequireUniqueEmail` is switched off because it only knows how to check *globally*, and
`TenantScopedUserValidator` plus a matching pair of `NormalizedEmail` indexes enforce the per-tenant
rule in its place. Uniqueness moved from *global* to *per-tenant*; it did not go away.

This section has sharp edges that will bite any consumer who doesn't know about them going in. Read all
of it before touching role assignment or login code built on `Slice.Authentication`.

### The NULL-uniqueness index gotcha

Enforcing "unique per tenant, but the same value may repeat across different tenants" sounds like a
single composite unique index — `UNIQUE (TenantId, NormalizedUserName)`. **That alone is not enough.**
Postgres (and most SQL databases) treat every `NULL` as distinct for uniqueness purposes: two rows with
`TenantId = NULL` and the *same* `NormalizedUserName` do **not** violate a composite unique index,
because `NULL <> NULL`. Left as just the one composite index, the platform tier (`TenantId IS NULL`)
would silently allow unlimited duplicate usernames.

`SliceAuthDbContext.OnModelCreating` uses two indexes per entity instead of one:

```csharp
// Non-null tenants: unique per (TenantId, NormalizedUserName)
b.HasIndex(u => new { u.TenantId, u.NormalizedUserName }, "UX_AspNetUsers_TenantId_NormalizedUserName")
    .IsUnique()
    .HasFilter(filters.TenantScoped);       // "TenantId" IS NOT NULL

// Platform tier: unique among themselves
b.HasIndex(u => u.NormalizedUserName, "UX_AspNetUsers_NormalizedUserName_PlatformOnly")
    .IsUnique()
    .HasFilter(filters.PlatformOnly);       // "TenantId" IS NULL
```

Same pattern for `SliceRole.NormalizedName`, and again for `SliceUser.NormalizedEmail` — the email
carries the same "once per tenant, repeatable across tenants" rule, and its two indexes additionally
exclude rows where the email is `NULL`, since Identity treats an email as optional and two accounts
without one must not collide.

Identity's own base `IdentityDbContext.OnModelCreating` still creates its original
`UserNameIndex`/`RoleNameIndex` — those are demoted to plain (non-unique) lookup indexes rather than
removed, since multi-tenant login still needs an efficient "every tenant matching this username" scan
across all tenants.

If you're hand-writing SQL against an existing database instead of letting `EnsureCreatedAsync` build a
fresh one (see "Migrating an existing deployment" below), reproduce this exactly — a single composite
index is a real, exploitable gap for the platform tier specifically.

### The filters are provider-specific SQL

`HasFilter` takes raw SQL, so the identifier quoting has to match the target database — and
`Slice.Authentication` is deliberately provider-agnostic (the host picks the provider via
`AddSliceAuthStore`). `TenantIndexFilters.For(Database.ProviderName)` supplies the right clause, the
same way `ConfigureExtraProperties(Database.ProviderName)` does one line above it:

| Provider | Quoting | Partial indexes |
|---|---|---|
| PostgreSQL, SQLite | `"TenantId"` | yes |
| SQL Server | `[TenantId]` | yes (double quotes parse as identifiers only under `SET QUOTED_IDENTIFIER ON`, which isn't guaranteed for whoever runs the DDL) |
| MySQL / MariaDB, Oracle | — | **no** |

On a provider without partial indexes only the unfiltered composite index is created. In-tenant rows are
still protected by the database; **platform-tier rows are not** — `NULL <> NULL` again — so there,
platform-tier uniqueness rests on `TenantScopedUserValidator`/`TenantScopedRoleValidator` alone. That's
a read-then-write application check, so two concurrent registrations can still both succeed. If you run
on one of those and care about the platform tier, add your own equivalent constraint (a computed
non-null discriminator column, or a trigger).

### EF Core footgun: demoting the base class's index without duplicating it

When you demote `IdentityDbContext`'s own `UserNameIndex`/`RoleNameIndex` to non-unique, the call
**must not** pass an explicit index name:

```csharp
// Correct — matches the base class's index by property list, reconfigures it in place.
b.HasIndex(u => u.NormalizedUserName).IsUnique(false);

// WRONG — creates a SECOND, independent index. The base class's original global-unique
// index is left fully intact underneath, silently defeating the entire point of this
// change: cross-tenant duplicate usernames still fail, just as before.
b.HasIndex(u => u.NormalizedUserName, "UserNameIndex").IsUnique(false);
```

`IdentityDbContext.OnModelCreating` declares these indexes as
`HasIndex(u => u.NormalizedUserName).HasDatabaseName("UserNameIndex").IsUnique()` — no explicit index
`Name`, only a database name override. EF's fluent API matches an existing index for reconfiguration by
property list plus explicit name (when given); since the base call never set an explicit name, a later
call that *does* pass one is treated as a different index, not a match. The result compiles, builds, and
even runs without error — the generated DDL just has two indexes on the same column, and only the DDL
diff or a duplicate-key error at insert time reveals the bug. Verify by inspecting the actual generated
schema (`\d "AspNetUsers"` in psql, `PRAGMA index_list('AspNetUsers')` in SQLite, or
`Database.GenerateCreateScript()`), not just a successful build — this exact bug shipped once and was
only caught that way.

### The role-name-ambiguity gotcha — read this before assigning roles

This is the single most important paragraph in this section.

Once two tenants can each have a role named "Manager" (or the platform tier has an "Admin" alongside
every tenant's own "Admin"), **every stock ASP.NET Identity API that resolves a role by bare name
becomes unsafe**:

- `RoleManager<TRole>.FindByNameAsync`
- `UserManager<TUser>.AddToRoleAsync` / `AddToRolesAsync` / `RemoveFromRoleAsync` / `RemoveFromRolesAsync` / `IsInRoleAsync`
- `UserManager<TUser>.GetUsersInRoleAsync`
- **`SignInManager<TUser>.SignInWithClaimsAsync` / `SignInAsync`, indirectly** — see "The hidden one"
  below; easy to miss because it doesn't look like a role lookup at the call site at all.

All of these do an internal, unfiltered `Roles.SingleOrDefaultAsync(r => r.NormalizedName == x)` (or
equivalent) with no tenant filter. The moment two roles share a normalized name, they throw
`InvalidOperationException: Sequence contains more than one element` — or, in APIs that don't use
`Single`, may silently resolve to the wrong tenant's role.

**Use `ITenantRoleAssigner` instead, everywhere a role needs to be resolved by name:**

```csharp
public interface ITenantRoleAssigner
{
    Task<SliceRole?> FindRoleAsync(string roleName, Guid? tenantId, CancellationToken ct = default);
    Task<List<SliceRole>> FindRolesAsync(IEnumerable<string> roleNames, Guid? tenantId, CancellationToken ct = default);
    Task<List<SliceRole>> GetRolesOfUserAsync(SliceUser user, CancellationToken ct = default);
    Task<IdentityResult> AddToRoleAsync(SliceUser user, string roleName, CancellationToken ct = default);
    Task<IdentityResult> AddToRolesAsync(SliceUser user, IEnumerable<string> roleNames, CancellationToken ct = default);
    Task<IdentityResult> RemoveFromRoleAsync(SliceUser user, string roleName, CancellationToken ct = default);
    Task<IdentityResult> RemoveFromRolesAsync(SliceUser user, IEnumerable<string> roleNames, CancellationToken ct = default);
    Task<bool> IsInRoleAsync(SliceUser user, string roleName, CancellationToken ct = default);
    Task<List<SliceUser>> GetUsersInRoleAsync(SliceRole role, CancellationToken ct = default);
    Task<int> CountUsersInRoleAsync(SliceRole role, CancellationToken ct = default);
}
```

Every by-name member scopes its lookup by `(TenantId, NormalizedName)` — always pass the *user's own*
`TenantId` (or the tenant you're operating on), never assume a bare name is unique. Names are normalized
through `RoleManager.KeyNormalizer` first, so lookups are case-insensitive exactly like the stock APIs.
An unknown name is a `RoleNotFound` failure, not a silent no-op, and a batch that contains one assigns
or removes nothing rather than committing part of itself.

Two members resolve by **link** rather than by name, and are the ones to reach for when the question is
"what roles does this user have" rather than "find the role called X":

- `GetRolesOfUserAsync(user)` — joins `AspNetUserRoles` by `RoleId`. Use it in preference to
  `FindRolesAsync(names, user.TenantId)`, which would drop a role deliberately granted from outside the
  user's own tenant (a platform-tier support role on in-tenant staff, say). `ConnectController` and
  `TenantSafeUserClaimsPrincipalFactory` both build their permission claims from this.
- `GetUsersInRoleAsync(role)` / `CountUsersInRoleAsync(role)` — take the resolved role, not a name.

`UserManager<TUser>.GetRolesAsync(user)` is **not** affected and needs no change — it joins through the
`AspNetUserRoles` foreign key table by `RoleId`, not by name, so it was never ambiguous. It returns
names, though, so feeding its output back into a by-name lookup reintroduces the problem;
`GetRolesOfUserAsync` avoids the round trip.

### The hidden one: the stock claims factory, via `SignInManager`

If your host app calls `SignInManager<TUser>.SignInWithClaimsAsync(user, isPersistent, additionalClaims)`
for cookie-based login (as opposed to building a `ClaimsPrincipal` by hand, the way `ConnectController`
does for the OAuth2 password grant), be aware it does **not** just apply `additionalClaims` on top of
nothing — it first builds a *base* principal via the registered `IUserClaimsPrincipalFactory<TUser>`. If
your `AddIdentity`/`AddIdentityCore` setup calls `.AddRoles<TRole>()` (needed for `RoleManager` etc.
regardless), ASP.NET Core registers the **role-aware** `UserClaimsPrincipalFactory<TUser, TRole>` by
default — and its `GenerateClaimsAsync` independently resolves each of the signed-in user's role *names*
via `RoleManager.FindByNameAsync`, the exact unsafe API listed above, merging in whatever role it
(silently, non-deterministically) resolves to.

This bit a real deployment: a platform-tier user's login ended up with an arbitrary *tenant's* full
permission set merged into their cookie, invisible until enough same-named roles existed across tenants
for the ambiguity to actually flip which row the unfiltered query happened to return first. It's easy to
miss during review because the vulnerable call is inside a *framework* method (`SignInWithClaimsAsync`)
your own code never explicitly names — nothing in the call site looks like a role-by-name lookup.

**Fix**: `SliceAuthenticationModule` registers `TenantSafeUserClaimsPrincipalFactory` in place of the
default. It inherits the **non**-role-aware `UserClaimsPrincipalFactory<SliceUser>` base (one type
parameter — no automatic role expansion) rather than the role-aware two-parameter one
`.AddRoles<SliceRole>()` installs, and re-adds the role, `tenant_id`, `permission`, `permission_deny`
and `data_scope` claims itself, resolving them through `ITenantRoleAssigner` against the user's actual
role links. The claim set matches what `ConnectController` puts in an access token, so a
cookie-authenticated principal and a bearer-authenticated one authorize identically.

This is **not** opt-in. The module also calls `.AddSignInManager()`, so leaving the stock factory in
place would make the framework's own default configuration the leaky path — an app would have to know
to fix it. To layer your own claims on top, subclass and re-register (single-instance resolution, so
last registration wins, no `RemoveAll` needed):

```csharp
services.AddScoped<IUserClaimsPrincipalFactory<SliceUser>, MyClaimsPrincipalFactory>();
```

### Login flow — password is the disambiguator

Since usernames/emails are no longer guaranteed globally unique, a bare
`FindByEmailAsync`/`FindByNameAsync` at login time can no longer safely resolve "the" user — there may
be more than one row with the same normalized username, one per tenant that user has an account in.

The pattern used in `ConnectController.Exchange()` (password grant) — and recommended for any
cookie-based login a host app builds on top of this module:

1. Query **every** `SliceUser` row matching the normalized username/email
   (`userManager.Users.Where(u => u.NormalizedUserName == x || u.NormalizedEmail == x)`), not
   `FindByEmailAsync`.
2. Check the submitted password against **each** candidate (`CheckPasswordAsync`) — each tenant's
   account has its own independent password hash, so this is the natural disambiguator for the
   overwhelmingly common case (a user only has one account, or only one candidate's password matches).
   **This is not free.** Verifying a password is a deliberately expensive hash, and this is one per
   candidate row, on an endpoint anyone can call without authenticating. An address shared by *N*
   tenants turns one request into *N* hashes. `ConnectController` therefore stops at
   `SliceAuthOptions.MaxLoginCandidates` (default 10) and rejects without hashing anything at all.
   The rejection is the **generic** invalid-credentials message, not a "too many accounts" hint: the
   cap is reached before any password is checked, so a specific message would let an unauthenticated
   caller measure how widely an address is used across tenants. A `tenant_id` parameter narrows the
   query *before* the cap applies, so a caller who supplies one is never capped — as is a host that
   scopes candidates to an already-resolved tenant by overriding `ResolveUserAsync` (see below), which
   is the right answer for any deployment where one address routinely spans many tenants. Raise the cap
   only if you also rate-limit the token endpoint.
3. Exactly one match → proceed as normal.
4. Zero matches → generic "invalid" — don't reveal whether the username or the password was wrong.
5. More than one match (rare: the *same* email and password happen to both be valid in more than one
   tenant) → this is genuinely ambiguous. By default, `ConnectController` rejects with a message asking
   for an OpenIddict `tenant_id` parameter on retry, and does **not** enumerate the candidate tenants in
   that response (an unauthenticated caller shouldn't be able to enumerate a user's tenant memberships by
   default). Once the password has already been verified against every candidate, the caller has proven
   they hold a valid credential for all of them, so a host app that decides that's an acceptable trade-off
   for its own UX can safely list the choices — see "Extending ConnectController" below for the hook.

`SliceClaims.TenantId` (`"tenant_id"`) is issued as a claim on successful sign-in whenever the resolved
user has a non-null `TenantId`.

One residual side channel is worth knowing about: response time still scales with how many accounts
share the submitted address (up to the cap), so a determined caller can infer roughly how many tenants
an address exists in even though the response body never says. Rate-limiting the token endpoint is the
practical answer; lowering `MaxLoginCandidates` narrows the range but doesn't close it.

### Extending `ConnectController`

`ConnectController` is not `sealed`, and its decision points are `protected virtual`, specifically so a
host app can layer in its own multi-tenancy signals without duplicating the whole OAuth2 exchange flow:

```csharp
protected UserManager<SliceUser> UserManager { get; }
protected SliceAuthOptions Options { get; }
protected ForbidResult Reject(string description);

protected virtual Task<LoginCandidates> ResolveUserAsync(
    string usernameOrEmail, string password, string? tenantIdHint);

protected virtual Task<IActionResult> RejectAmbiguousAsync(IReadOnlyList<SliceUser> candidates);

protected virtual Task<ClaimsPrincipal> CreatePrincipalAsync(SliceUser user, ImmutableArray<string> scopes);
```

`LoginCandidates` carries the accounts whose password matched plus an `ExceededCandidateLimit` flag —
returning the flag rather than throwing keeps this on the framework's "business outcomes are values,
only faults throw" rule, so `Exchange()` can turn it into a normal `invalid_grant` response.

Common reasons to subclass:

- **Scope candidates to an already-resolved ambient tenant.** If the host app also references
  `Slice.MultiTenancy` and has a request-level tenant signal (e.g. subdomain, custom domain, or the
  `X-Tenant-Id` header, resolved via `ICurrentTenant` *before* this controller runs — see "Resolving the
  tenant" below), override `ResolveUserAsync` to filter `UserManager.Users` by `ICurrentTenant.Id` before
  the password check, on top of (or instead of) the existing `tenant_id` parameter filter. This closes a
  real gap: without it, a client that reaches a tenant-specific host/header still authenticates against
  *any* tenant a matching username/password belongs to, not just the one implied by how it got there.
- **Enrich the ambiguous-match response.** Override `RejectAmbiguousAsync` to return the actual list of
  matching tenants (id + display name, via whatever tenant-registry lookup the host app has — e.g.
  `Slice.Management`'s `ITenantManager`) instead of the generic message, so a client can render a picker
  directly rather than needing the caller to already know an out-of-band tenant identifier.
- **Add your own claims to the token.** Override `CreatePrincipalAsync`, calling the base first unless
  you mean to replace the tenant/role/permission set wholesale. If you also use cookie sign-in, make the
  equivalent change in a `TenantSafeUserClaimsPrincipalFactory` subclass so both paths agree.

Because a controller can't have two competing route registrations for the same path, a host app
replacing `ConnectController` needs to remove the original from MVC discovery — e.g. via
`ConfigureApplicationPartManager`, stripping the `Slice.Authentication` `ApplicationPart` before adding
the replacement controller's own assembly/part.

### Free interop with row-level tenant filtering

`SliceClaims.TenantId` and `Slice.MultiTenancy.TenantConstants.Claim` are the **same** underlying
constant (`Slice.Domain.MultiTenancy.MultiTenancyClaims.TenantId`), not two independent literals that
happen to match. That means: the moment a host app references `Slice.MultiTenancy` and wires
`app.UseSliceMultiTenancy()`, `ClaimTenantResolveContributor` (see "Resolving the tenant" below)
automatically picks up the `tenant_id` claim `Slice.Authentication` issues at login — tenant-scoped
identity drives the ambient `ICurrentTenant` for every request, and from there, row-level `IMultiTenant`
filtering on every other entity, with **no extra glue code**.

Both sides reference the same constant, so the literals cannot drift. What the claim carries is covered
by `ConnectControllerTests` (a `tenant_id` claim is issued for an in-tenant user and absent for a
platform one) and `TenantSafeUserClaimsPrincipalFactoryTests` (same for cookie sign-in). The remaining
hop — that claim driving `ICurrentTenant` and therefore row-level filtering inside a running host — has
no automated coverage yet, because no sample wires `Slice.Authentication` and `Slice.MultiTenancy`
together. Confirm it directly the first time you do.

### Migrating an existing deployment

`SliceAuthDbContext` is **not** migration-driven — `SliceAuthDataSeeder.SeedAsync` calls
`Database.EnsureCreatedAsync()`, and `Slice.Authentication.csproj` has no Npgsql/SQL Server/etc.
provider package reference of its own (it's provider-agnostic; the host binds a provider). That means:

- **A fresh database** picks up the new schema automatically — nothing to do.
- **An existing database does not get migrated automatically.** `EnsureCreatedAsync()` is a no-op once
  the database already exists — this includes the case where you drop just the identity tables from an
  otherwise-existing database; `EnsureCreatedAsync()` checks whether the *database* exists, not whether
  specific tables do, so dropping tables alone will not trigger a rebuild. You must apply the schema
  change yourself. Exact Postgres DDL matching the C# index design above (add the `TenantId` columns
  used elsewhere in this doc's index examples, plus the `DataProtectionKeys` table if you also adopt
  `IDataProtectionKeyContext`):

  ```sql
  ALTER TABLE "AspNetUsers" ADD COLUMN IF NOT EXISTS "TenantId" uuid NULL;
  ALTER TABLE "AspNetRoles" ADD COLUMN IF NOT EXISTS "TenantId" uuid NULL;

  DROP INDEX IF EXISTS "UserNameIndex";
  CREATE INDEX "UserNameIndex" ON "AspNetUsers" ("NormalizedUserName");
  CREATE UNIQUE INDEX "UX_AspNetUsers_TenantId_NormalizedUserName" ON "AspNetUsers" ("TenantId","NormalizedUserName") WHERE "TenantId" IS NOT NULL;
  CREATE UNIQUE INDEX "UX_AspNetUsers_NormalizedUserName_PlatformOnly" ON "AspNetUsers" ("NormalizedUserName") WHERE "TenantId" IS NULL;

  CREATE UNIQUE INDEX "UX_AspNetUsers_TenantId_NormalizedEmail" ON "AspNetUsers" ("TenantId","NormalizedEmail") WHERE "TenantId" IS NOT NULL AND "NormalizedEmail" IS NOT NULL;
  CREATE UNIQUE INDEX "UX_AspNetUsers_NormalizedEmail_PlatformOnly" ON "AspNetUsers" ("NormalizedEmail") WHERE "TenantId" IS NULL AND "NormalizedEmail" IS NOT NULL;

  DROP INDEX IF EXISTS "RoleNameIndex";
  CREATE INDEX "RoleNameIndex" ON "AspNetRoles" ("NormalizedName");
  CREATE UNIQUE INDEX "UX_AspNetRoles_TenantId_NormalizedName" ON "AspNetRoles" ("TenantId","NormalizedName") WHERE "TenantId" IS NOT NULL;
  CREATE UNIQUE INDEX "UX_AspNetRoles_NormalizedName_PlatformOnly" ON "AspNetRoles" ("NormalizedName") WHERE "TenantId" IS NULL;
  ```
  Every pre-existing row gets `TenantId = NULL`, i.e. becomes platform-tier — decide deliberately
  whether that's the right classification for your existing users/roles, or whether some of them should
  be back-filled with a real tenant ID before you start relying on the distinction.
- **If your consuming app targets a non-Postgres provider**, translate the partial-index (`WHERE`)
  syntax accordingly — SQL Server uses `CREATE UNIQUE INDEX ... WHERE ...` too (filtered indexes),
  SQLite supports partial indexes with the same `WHERE` syntax; the concept translates, the exact DDL
  may not.
- Consider switching this store to real EF Core migrations if you need repeatable, versioned schema
  changes going forward — it currently deliberately avoids that to stay provider-agnostic without
  per-provider migration assemblies.

### Breaking changes summary

- `o.User.RequireUniqueEmail` (in `SliceAuthenticationModule.ConfigureServices`) now defaults to
  `false` instead of `true` — replaced by the per-tenant email check in `TenantScopedUserValidator`
  and the `(TenantId, NormalizedEmail)` index pair. A duplicate email inside one tenant now fails with
  `DuplicateEmail` rather than Identity's own error; the same address across two tenants now succeeds.
- `SliceAuthenticationModule` now registers `TenantSafeUserClaimsPrincipalFactory` as
  `IUserClaimsPrincipalFactory<SliceUser>`, and calls
  `AddDataProtection().PersistKeysToDbContext<SliceAuthDbContext>()`. Both were previously left to the
  host; a host already doing either keeps winning, since it registers after the module.
  **Check the Data Protection one before upgrading.** If your host configured a keyring *before*
  `AddSliceModules` — `PersistKeysToFileSystem`, Azure Blob, Redis — the module's registration now
  wins, the keyring moves to an empty `DataProtectionKeys` table, and every existing auth cookie or
  other protected payload becomes undecryptable. Move your `AddDataProtection()` call after
  `AddSliceModules` to keep it.
- **Existing access tokens carry no `tenant_id` claim**, so their bearers are treated as platform-tier
  until they re-authenticate — including by `IdentityManagementController`, which lets a platform-tier
  caller name any target tenant. Ephemeral signing keys (the default) rotate on restart and invalidate
  old tokens anyway; a host that has registered persistent signing certificates should force
  re-issue as part of the upgrade.
- `SliceUser`/`SliceRole`/`SliceAuthDbContext` are no longer `sealed`.
- Any code calling `RoleManager<SliceRole>.FindByNameAsync`,
  `UserManager<SliceUser>.AddToRoleAsync`/`AddToRolesAsync`/`RemoveFromRoleAsync`/`RemoveFromRolesAsync`/`GetUsersInRoleAsync`
  directly must switch to `ITenantRoleAssigner` (see above) — the old calls will throw or behave
  incorrectly the moment more than one role/user shares a normalized name across tenants.
- New public surface: `ITenantRoleAssigner`/`TenantRoleAssigner`, `TenantScopedUserValidator`/`TenantScopedRoleValidator`,
  `SliceClaims.PermissionDeny`/`DataScope`/`TenantId`, `Slice.Domain.MultiTenancy.MultiTenancyClaims`.
- `ConnectController` is no longer `sealed`. It gains `protected virtual ResolveUserAsync` (returning
  the new `LoginCandidates` record), `protected virtual RejectAmbiguousAsync`, `protected virtual
  CreatePrincipalAsync`, a `protected Reject(string)` helper, and `protected UserManager`/`Options`
  properties. Its constructor now also takes `ITenantRoleAssigner` and `SliceAuthOptions`. Default
  behavior for any consumer not subclassing is unchanged. See "Extending ConnectController" above.
- `ITenantRoleAssigner` gained `GetRolesOfUserAsync`, `RemoveFromRoleAsync` (singular), `IsInRoleAsync`
  and `GetUsersInRoleAsync`, so it now covers every stock API listed above. `FindRolesAsync` matches on
  `NormalizedName` (it previously compared the raw `Name`, so lookups were case-sensitive and disagreed
  with `FindRoleAsync`). `RemoveFromRolesAsync`/`AddToRolesAsync` now fail with `RoleNotFound` on an
  unknown name instead of silently doing nothing / committing a partial batch.
- `Slice.Management`'s `IdentityManagementController` now stamps `TenantId` on the users and roles it
  creates and resolves roles via `ITenantRoleAssigner`. `CreateUserRequest`/`CreateRoleRequest` gained
  an optional `TenantId`, honored only for platform-tier callers. Previously every principal created
  through the management API was platform-tier regardless of the calling tenant.
- New public surface: `TenantSafeUserClaimsPrincipalFactory` (registered by default, see above),
  `LoginCandidates`, `SliceAuthOptions.MaxLoginCandidates`.

---

## Resolving the tenant

For HTTP requests, `MultiTenancyMiddleware` runs the resolver and pushes the result via
`ICurrentTenant.Change(...)` for the request. Wire it after authentication:

```csharp
app.UseSliceAuthentication();
app.UseSliceMultiTenancy();      // resolves + sets the ambient tenant for the request
```

Resolution runs a chain of **contributors** in `Order`; the first to produce a tenant wins:

| Contributor | Source | Key | `Order` |
|---|---|---|---|
| `ClaimTenantResolveContributor` | the authenticated principal | `tenant_id` claim | `TenantResolveOrder.Claim` (100) |
| `HeaderTenantResolveContributor` | the request headers | `X-Tenant-Id` | `TenantResolveOrder.Header` (900) |

```csharp
public interface ITenantResolveContributor
{
    int Order => TenantResolveOrder.Default;   // 500
    Task ResolveAsync(TenantResolveResult result, CancellationToken ct);
}
public interface ITenantResolver { Task<TenantResolveResult> ResolveAsync(CancellationToken ct = default); }
```

**This ordering is a trust boundary, not a preference.** Because the first contributor to resolve wins,
whichever runs first *decides* the tenant. The `tenant_id` claim arrives in a signed token the caller
cannot alter; `X-Tenant-Id` is a plain request header the caller fully controls. If the header ran
first, any authenticated user could move themselves into another tenant with one header — and since
`ICurrentTenant` drives every `IMultiTenant` query filter, the whole request would then read and write
that tenant's rows. The claim therefore runs first, and the header only gets a say when nothing more
trustworthy resolved (unauthenticated tenant routing, or a platform-tier operator with no tenant claim
of their own choosing a tenant to work in).

`Order` is explicit for exactly this reason — the chain used to run in DI registration order, which is
assembly scan order, and that is not something to rest a trust decision on.

Add your own contributor (e.g. subdomain or route) by implementing `ITenantResolveContributor` with a
marker so it's registered, and give it an `Order` that reflects how much you trust its source: below
`Header` if the caller can't forge it (subdomain routed by your load balancer, client certificate),
above `Claim` only if it genuinely should override a user's own token.

The `MultiTenancyBehavior` (pipeline order 200) ensures the ambient tenant is established for requests
that enter through the mediator outside of HTTP.

---

## Row-level isolation (shared database)

Any entity that implements `IMultiTenant` gets a global query filter:

```csharp
e => !IsMultiTenantFilterEnabled || EF.Property<Guid?>(e, "TenantId") == CurrentTenantId;
```

So `db.Leads` automatically returns only the current tenant's rows. When creating an entity, stamp its
`TenantId` from `ICurrentTenant.Id`:

```csharp
var lead = new Lead(guids.Create(), tenant.Id, name, contact, source);
```

You can temporarily disable the filter for cross-tenant/host operations via `IDataFilter` (the same
mechanism that controls the soft-delete filter).

This is the default and needs no extra configuration beyond implementing `IMultiTenant` and referencing
`Slice.MultiTenancy`.

---

## Database-per-tenant (isolated database)

> Runnable example: [`samples/Slice.Sample.MultiTenant`](../samples/Slice.Sample.MultiTenant/) — a
> SQLite file per tenant, resolved from the `X-Tenant-Id` header.

When tenants need physically separate databases, register the context with
`AddSliceMultiTenantDbContext` and provide the host/default connection string plus a provider applier.
The connection string is resolved **per scope** from the current tenant.

```csharp
// host/default connection string + how to apply the resolved string to the provider
services.AddSliceMultiTenantDbContext<CrmDbContext>(
    defaultConnectionString: config.GetConnectionString("Host")!,
    configure: (options, connectionString) => options.UseSqlite(connectionString));

// supply the tenant → connection-string map (in-memory here; back it with your own store in production)
services.AddTenantConnectionStrings(new Dictionary<Guid, string>
{
    [tenantA] = "Data Source=tenant-a.db",
    [tenantB] = "Data Source=tenant-b.db",
});
```

How it resolves, per request scope:

```
ICurrentTenant.Id ──► ITenantConnectionResolver.Resolve(tenantId, defaultConnectionString)
                          │
                          ├─ ITenantConnectionStore.Find(tenantId) returns a dedicated DB? → use it
                          └─ otherwise → use the host/default connection string
```

Seams you can replace:

| Type | Default | Purpose |
|---|---|---|
| `ITenantConnectionStore` | `NullTenantConnectionStore` (everyone shares the host DB) | map tenant → connection string |
| `InMemoryTenantConnectionStore` | — | a ready-made store seeded from a dictionary (dev/demo) |
| `ManagementTenantConnectionStore` | — | **production**: cached, reads each tenant's connection string from the `SliceTenants` registry — `AddSliceManagementTenantConnectionStore()` (needs `AddSliceManagementStore`) |
| `ITenantConnectionResolver` | `TenantConnectionResolver` | combine the store + the default |

For production, prefer the registry-backed `ManagementTenantConnectionStore` over a hard-coded
dictionary: tenants and their connection strings live in the `SliceTenants` table (set
`TenantRecord.ConnectionString`), so tenants can be onboarded at runtime — see the
[`Slice.Sample.MultiTenant`](../samples/Slice.Sample.MultiTenant/) sample's `POST /api/tenants`.

### Migrating tenant databases

Each tenant's database needs its schema created and **kept up to date as the model changes**. Prefer EF
**migrations** (`Database.MigrateAsync()`) over `EnsureCreatedAsync()`: migrations write
`__EFMigrationsHistory` and evolve an existing schema, whereas `EnsureCreated` only creates a brand-new
database once and never alters it.

`Slice.Management` ships a reusable `ITenantDatabaseMigrator` that applies migrations across the
host/default database **and** every tenant in the `SliceTenants` registry. Register it next to the
multi-tenant context (it needs `AddSliceManagementStore` for the registry):

```csharp
services.AddSliceMultiTenantDbContext<TenantDbContext>(
    defaultConnectionString: "Data Source=tenant-host.db",
    configure: (options, cs) => options.UseSqlite(cs));
services.AddSliceTenantDatabaseMigrator<TenantDbContext>();
```

Migrate every database on startup (host + all registered tenants), e.g. from a module's
`OnApplicationInitializationAsync`:

```csharp
using var scope = context.ServiceProvider.CreateScope();
await scope.ServiceProvider.GetRequiredService<ITenantDatabaseMigrator>().MigrateAllAsync();
```

…and provision a single tenant when onboarding it:

```csharp
var tenant = await tenants.CreateAsync(name, connectionString, ct);   // ITenantManager → SliceTenants
connectionStore.Invalidate(tenant.Id);                                 // reload the resolver cache
await migrator.MigrateTenantAsync(tenant.Id, ct);                      // create/upgrade that tenant's DB
```

Internally the migrator switches `ICurrentTenant.Change(tenantId)` and resolves `TContext` in a child
scope, so the connection resolver hands it that tenant's connection string before
`Database.MigrateAsync()` runs. Because `MigrateAllAsync()` walks the whole registry, a migration you add
later is applied to **all** existing tenant databases on the next startup. The runnable
[`Slice.Sample.MultiTenant`](../samples/Slice.Sample.MultiTenant/) sample uses exactly this (an
`InitialCreate` migration, `MigrateAllAsync()` at startup, `MigrateTenantAsync(id)` in `POST /api/tenants`).

> Generating migrations for a multi-tenant context: add an `IDesignTimeDbContextFactory<TContext>` that
> builds the context against the host connection string, so `dotnet ef migrations add …` doesn't need a
> running app or an ambient tenant. See the sample's `TenantDbContextDesignTimeFactory`.

#### At scale: migrate from a separate process

Running `MigrateAllAsync()` in the serving app at startup is fine for dev and small/fixed fleets, but for
many tenants or multiple replicas it couples schema evolution to request serving — it delays readiness,
every replica races, and one tenant's failure can abort startup for all. For production, **decouple
migration from serving**:

- Set `MultiTenant:RunMigrationsOnStartup=false` so the serving app never migrates in-process.
- Run migrations from a dedicated job/executable (a deploy step or Kubernetes `Job`) **before** rolling
  out the app. The runnable [`Slice.Sample.MultiTenant.Migrator`](../samples/Slice.Sample.MultiTenant.Migrator/)
  console project composes the same module graph and calls `MigrateAllAsync(...)`, returning a non-zero
  exit code if any tenant fails.

The options overload tunes a fleet run and returns a per-tenant `TenantMigrationReport`:

```csharp
var report = await migrator.MigrateAllAsync(new TenantMigrationOptions
{
    MaxDegreeOfParallelism = 4,   // migrate tenants concurrently (default 1 = sequential)
    ContinueOnError = true,       // attempt every tenant; collect failures instead of aborting (default false = fail-fast)
    UseDistributedLock = true,    // single-runner guard via IDistributedLock (no-op default; register Redis for real coordination)
});
// report.Succeeded / report.Failed / report.Results[]; report.LockNotAcquired when another runner holds the lock
```

The default parameterless `MigrateAllAsync()` stays sequential + fail-fast (the startup path). Onboarding
a single new tenant still uses `MigrateTenantAsync(id)` inline — it's one database, triggered by an
explicit admin action, not the whole fleet.

When several migration jobs might run at once (e.g. one per replica in a rolling deploy), set
`UseDistributedLock = true` and register a real `IDistributedLock` so only one runner migrates the fleet;
the others get `report.LockNotAcquired == true` and no-op. The sample Migrator wires Redis when
`ConnectionStrings:Redis` (env `ConnectionStrings__Redis`) is set — `services.AddSliceRedisDistributedLock(cs)`
after `AddSliceModules` — and falls back to the no-op lock otherwise.

> The Redis lock (`Slice.DistributedLocking.Redis`) holds a fixed 30-second TTL with no auto-renewal, and
> `TenantMigrationOptions.LockTimeout` is the *acquire-wait*, not the hold time. That's enough to
> coordinate job starts, but a fleet migration running longer than the TTL can have its lock expire
> mid-run — for long migrations use a renewing/longer-TTL lock implementation.

### When to use which

| | Row-level | Database-per-tenant |
|---|---|---|
| Isolation | logical (query filter) | physical (separate DB) |
| Ops cost | one database | one database per tenant |
| Noisy-neighbour / compliance | weaker | stronger |
| Per-tenant backup/restore | hard | easy |
| Setup | implement `IMultiTenant` | `AddSliceMultiTenantDbContext` + a connection store |

The two are not mutually exclusive: a database-per-tenant context can still hold `IMultiTenant`
entities (the filter is a harmless no-op when the database already contains only one tenant's data).
