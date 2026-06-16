# Slice sample — Database-per-tenant

One `TenantDbContext` whose **connection string is resolved per request from the current tenant**, so
each tenant's data lives in its **own physical database**. The tenant is taken from the `X-Tenant-Id`
header; isolation is by database, not just by a row filter.

The tenant→database map is **not hard-coded** — it lives in a **registry database** (`SliceTenants`)
and is read (and cached) by `ManagementTenantConnectionStore`. Tenants can be **onboarded at runtime**,
which provisions a brand-new database with no code change or redeploy.

How it's wired (`TenantModule`):

```csharp
// the registry (SliceTenants) is the source of truth for tenant → connection string
services.AddSliceManagementStore(o => o.UseSqlite("Data Source=tenant-registry.db"));
services.AddSliceManagementTenantConnectionStore();   // cached, DB-backed ITenantConnectionStore

services.AddSliceMultiTenantDbContext<TenantDbContext>(
    defaultConnectionString: "Data Source=tenant-host.db",
    configure: (options, connectionString) => options.UseSqlite(connectionString));
```

`UseSliceMultiTenancy()` resolves the tenant from `X-Tenant-Id` (the built-in
`HeaderTenantResolveContributor`) and sets `ICurrentTenant`; the multi-tenant DbContext then looks up
that tenant's connection string in the registry (cached; reloaded on miss) and connects to its
database. A tenant with no connection string falls back to `tenant-host.db`.

## Demo tenants (seeded into the registry at startup)

| Tenant | `X-Tenant-Id` | Database |
|---|---|---|
| A | `11111111-1111-1111-1111-111111111111` | `tenant-a.db` |
| B | `22222222-2222-2222-2222-222222222222` | `tenant-b.db` |

## Onboard a tenant at runtime

```bash
curl -X POST http://localhost:5000/api/tenants -H "Content-Type: application/json" -d '{"name":"Acme Corp"}'
# → { "id": "<new-guid>", "connectionString": "Data Source=tenant-<id>.db" }
```

This registers the tenant in `SliceTenants` and **provisions its dedicated database on the fly**. Use
the returned id as `X-Tenant-Id` and its data is fully isolated in `tenant-<id>.db`. (In production the
connection string would come from your infrastructure/secret store rather than being derived from the id.)

## Run it

```bash
dotnet run --project samples/Slice.Sample.MultiTenant
A=11111111-1111-1111-1111-111111111111
B=22222222-2222-2222-2222-222222222222

curl -X POST http://localhost:5000/api/widgets -H "X-Tenant-Id: $A" -H "Content-Type: application/json" -d '{"name":"Alpha-Widget"}'
curl -X POST http://localhost:5000/api/widgets -H "X-Tenant-Id: $B" -H "Content-Type: application/json" -d '{"name":"Beta-Thing"}'

curl http://localhost:5000/api/widgets -H "X-Tenant-Id: $A"   # only Alpha-*
curl http://localhost:5000/api/widgets -H "X-Tenant-Id: $B"   # only Beta-*
```

`tenant-a.db` and `tenant-b.db` are distinct files, each holding only that tenant's rows.

See also: [docs/multitenancy.md](../../docs/multitenancy.md#database-per-tenant-isolated-database).
