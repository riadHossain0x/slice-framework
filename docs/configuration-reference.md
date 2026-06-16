# Configuration reference

Every configuration key the framework reads from `IConfiguration` (`appsettings.json`, environment
variables, etc.). Keys not listed here are options classes configured in code via `AddSlice…(o => …)`.

> Environment-variable form: replace `:` with `__` (e.g. `Settings:Crm.AutoArchiveDays` →
> `Settings__Crm.AutoArchiveDays`).

---

## Connection strings

Connection strings are read by your modules (the sample and the `slice-api` template use these names).
Each `AddSlice…Store`/`AddSliceDbContext` call decides which name it reads.

| Key | Used by | Example |
|---|---|---|
| `ConnectionStrings:App` (or your name) | the application `DbContext` | `Data Source=app.db` |
| `ConnectionStrings:Auth` | `Slice.Authentication` store | `Data Source=auth.db` |
| `ConnectionStrings:Management` | `Slice.Management` store | `Data Source=mgmt.db` |
| `ConnectionStrings:Host` | database-per-tenant default | `Data Source=host.db` |
| `ConnectionStrings:Redis` | Redis cache / lock adapters | `localhost:6379` |

```json
{
  "ConnectionStrings": {
    "App": "Data Source=app.db",
    "Auth": "Data Source=auth.db",
    "Management": "Data Source=mgmt.db"
  }
}
```

---

## Authentication — `SliceAuth`

`SliceAuthOptions` is bound from the `SliceAuth` section.

| Key | Default | Meaning |
|---|---|---|
| `SliceAuth:ConnectionString` | `Data Source=auth.db` | identity/OpenIddict store |
| `SliceAuth:SeedDemoAdmin` | `true` | seed the demo admin on startup |
| `SliceAuth:AdminEmail` | `admin@slice` | demo admin login |
| `SliceAuth:AdminPassword` | `Admin123!` | demo admin password |
| `SliceAuth:AdminRole` | `admin` | demo admin role (granted every declared permission) |

```json
{ "SliceAuth": { "SeedDemoAdmin": true, "AdminEmail": "admin@slice", "AdminPassword": "Admin123!" } }
```

> For production, set `SeedDemoAdmin: false`, supply real signing/encryption certificates (replace the
> dev ephemeral keys), and seed real principals. See [Security](security.md#production-hardening).

---

## Authorization — `Authorization`

Read by the default `ConfigurationPermissionStore` (when no claims/DB store is active).

| Key | Type | Meaning |
|---|---|---|
| `Authorization:GrantedPermissions` | string array | permissions granted to everyone |

```json
{ "Authorization": { "GrantedPermissions": [ "Crm.Leads.View" ] } }
```

Permissions declared with `GrantedByDefault: true` in their `PermissionDefinition` are always granted
by the configuration store regardless of this list. (When `Slice.Authentication` or `Slice.Management`
is present, their stores take over and this section is no longer the source of truth.)

---

## Settings — `Settings`

The configuration setting-value provider (order 10) reads `Settings:{name}`.

| Key | Meaning |
|---|---|
| `Settings:<SettingName>` | value for a defined setting (overrides its default; overridden by the global/management stores) |

```json
{ "Settings": { "Crm.AutoArchiveDays": "45" } }
```

See [Cross-cutting services → Settings](cross-cutting-services.md#settings) for the full provider
precedence (management −10 → global 0 → configuration 10 → default 100).

---

## Features — `Features`

The configuration feature store reads `Features:{name}` (after the management DB store, if present).

| Key | Meaning |
|---|---|
| `Features:<FeatureName>` | `"true"`/`"false"` (or a value) for a defined feature |

```json
{ "Features": { "Crm.BetaPipeline": "true" } }
```

> Environment override note: feature names contain dots (`Features:Crm.BetaPipeline`); via environment
> variable that is `Features__Crm.BetaPipeline`.

---

## Blob storage — `BlobStoring`

Read by the default file-system provider.

| Key | Default | Meaning |
|---|---|---|
| `BlobStoring:FileSystem:BasePath` | `{AppContext.BaseDirectory}/blobs` | root folder for blobs |

Cloud backends (AWS/Azure/MinIO) are configured in code via their `AddSliceBlobStoring…` calls, not
through configuration.

---

## Logging — `Serilog`

When you call `builder.UseSliceSerilog()`, Serilog reads the standard `Serilog` section
(`ReadFrom.Configuration`) for sinks, minimum levels, and enrichers — in addition to the
`Enrich.FromLogContext()` and providers Slice wires.

```json
{
  "Serilog": {
    "MinimumLevel": { "Default": "Information", "Override": { "Microsoft.AspNetCore": "Warning" } },
    "WriteTo": [ { "Name": "Console" } ]
  }
}
```

The standard `Logging` section still applies when not using Serilog.

---

## Options configured in code (not configuration)

These have no configuration binding; set them through their registration extension:

| Area | Options type | Registered via |
|---|---|---|
| Caching default TTL (10 min) | — (constant in `SliceCache`) | n/a |
| Redis cache | — | `AddSliceRedisCache(connectionString, instanceName?)` |
| Redis lock | — | `AddSliceRedisDistributedLock(connectionString)` |
| SMTP email | `SmtpEmailOptions` | `AddSliceSmtpEmailSender(o => …)` |
| MailKit email | `MailKitEmailOptions` | `AddSliceMailKit(o => …)` |
| RabbitMQ | `RabbitMqOptions` | `AddSliceRabbitMq(o => …)` |
| Azure Service Bus | `AzureServiceBusOptions` | `AddSliceAzureServiceBus(o => …)` |
| Kafka | `KafkaConnectionOptions` + `KafkaEventBusOptions` | `AddSliceKafkaEventBus(conn, bus)` |
| AWS S3 | `AwsS3BlobOptions` | `AddSliceBlobStoringAws(bucket, client?)` |
| Azure Blob | — | `AddSliceBlobStoringAzure(connectionString)` |
| MinIO | `MinioBlobOptions` (via builder) | `AddSliceBlobStoringMinio(o => …)` |
| Localization default culture (`en`) | `LocalizationOptions` | localization module options |
| API versioning | — | `AddSliceApiVersioning()` |
| Tenant connection strings | `ITenantConnectionStore` | `AddTenantConnectionStrings(map)` |
