# Multi-tenancy

Slice supports **multi-tenancy** at two levels of isolation, both driven by the same ambient
`ICurrentTenant`:

- **Row-level isolation** (shared database) — a global query filter restricts `IMultiTenant` entities
  to the current tenant. This is on by default for any entity that implements `IMultiTenant`.
- **Database-per-tenant** (isolated database) — the `DbContext` connection string is resolved per
  request from the current tenant.

Packages: `Slice.MultiTenancy` (resolution, ambient, middleware), `Slice.EntityFrameworkCore`
(the filters + the per-tenant connection plumbing).

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

## Resolving the tenant

For HTTP requests, `MultiTenancyMiddleware` runs the resolver and pushes the result via
`ICurrentTenant.Change(...)` for the request. Wire it after authentication:

```csharp
app.UseSliceAuthentication();
app.UseSliceMultiTenancy();      // resolves + sets the ambient tenant for the request
```

Resolution runs a chain of **contributors** in order; the first to produce a tenant wins:

| Contributor | Source | Key |
|---|---|---|
| `ClaimTenantResolveContributor` | the authenticated principal | `tenant_id` claim |
| `HeaderTenantResolveContributor` | the request headers | `X-Tenant-Id` |

```csharp
public interface ITenantResolveContributor { Task ResolveAsync(TenantResolveResult result, CancellationToken ct); }
public interface ITenantResolver         { Task<TenantResolveResult> ResolveAsync(CancellationToken ct = default); }
```

Add your own contributor (e.g. subdomain or route) by implementing `ITenantResolveContributor` with a
marker so it's registered; order it relative to the built-ins.

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
