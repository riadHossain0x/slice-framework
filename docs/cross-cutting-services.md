# Cross-cutting services

Reference for the supporting capabilities. Each has its own package README (linked) with the exact
API; this page shows where each fits and the common usage. All follow the same pattern: a seam with a
zero-config default, optional adapters, and convention-based registration.

- [Settings](#settings) · [Features](#features) · [Localization](#localization) ·
  [Caching](#caching) · [Blob storage](#blob-storage) · [Email](#email) ·
  [Background jobs](#background-jobs) · [Background workers](#background-workers) ·
  [Distributed locking](#distributed-locking) · [Object mapping](#object-mapping) ·
  [API versioning](#api-versioning) · [Virtual file system](#virtual-file-system) ·
  [Serilog](#serilog) · [SignalR](#signalr)

---

## Settings

`Slice.Settings` — named configuration values resolved through a **layered provider chain**, lowest
`Order` wins. ([README](../src/Slice.Settings/README.md))

Define settings via a `SettingDefinitionProvider`; read them via `ISettingManager`:

```csharp
public sealed class CrmSettings : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
        => context.Add(new SettingDefinition("Crm.AutoArchiveDays", defaultValue: "30"));
}

// reading
var days = await settingManager.GetAsync<int>("Crm.AutoArchiveDays");   // T : IParsable<T>
```

Provider precedence (lowest order first):

| Order | Provider | Source |
|---|---|---|
| −10 | `ManagementSettingValueProvider` (`Slice.Management`) | DB: user → tenant → global |
| 0 | global store | `IGlobalSettingStore` |
| 10 | configuration | `Settings:{name}` in `appsettings` |
| 100 | default | the `SettingDefinition.DefaultValue` |

---

## Features

`Slice.Features` — boolean/value **feature flags** with a definition + check + the `[RequiresFeature]`
pipeline gate (order 350). ([README](../src/Slice.Features/README.md))

```csharp
public sealed class CrmFeatures : FeatureDefinitionProvider
{
    public override void Define(IFeatureDefinitionContext context)
        => context.Add(new FeatureDefinition("Crm.BetaPipeline"));   // default "false"
}

[RequiresFeature("Crm.BetaPipeline")]
public sealed record UseBetaPipelineCommand(/* … */) : ICommand<Result>;
```

A disabled feature short-circuits to `Error.Forbidden("Features:Disabled", …)` → **403**. Values come
from `IFeatureStore`: DB (management, tenant → global) → `Features:{name}` configuration → default.
Check imperatively with `IFeatureChecker.IsEnabledAsync(name)`.

The attribute goes on the **request** (command/query), so it gates both controllers and minimal-API
endpoints (they all dispatch through `ISender`). To gate a **whole module** without annotating every
slice, register it once in the module — every request in that module's **assembly** then requires the
feature (it composes with any per-slice `[RequiresFeature]`):

```csharp
public override void ConfigureServices(ServiceConfigurationContext context)
    => context.Services.RequireFeature<SalesModule>("Sales");   // gates the whole Sales module
```

(Keyed on the request type's assembly — the module-per-project convention. In a single-project app where
"modules" are namespaces, use the per-slice attribute instead.)

### Managing setting & feature values (DB)

`Slice.Management` exposes write-side managers for the DB-backed value tables, plus admin endpoints
(`api/management/settings`, `api/management/features` — `GET`/`PUT`/`DELETE`):

```csharp
await featureValues.SetAsync("Sales", "true", providerName: "T", providerKey: tenantId.ToString());  // enable per tenant
await featureValues.ClearAsync("Sales", "T", tenantId.ToString());                                    // back to default
await settingValues.SetAsync("Crm.MaxLeadsPerDay", "45", "T", tenantId.ToString());
```
Provider scopes: settings `G`/`T`/`U`, features `G`/`T`.

### Feature-based module entitlement per tenant

Combine the two to grant **whole modules** to specific tenants in a modular app:

1. Each module declares a feature (`FeatureDefinitionProvider`) and gates its slices with
   `[RequiresFeature("Sales")]` (runtime 403 when off).
2. Tie the module's **permission group** to the same feature so its permissions don't surface for tenants
   that lack it: `context.AddGroup("Sales").RequireFeature("Sales").AddPermission("Sales.Orders.Create")`.
   Permissions inherit the group's `RequiredFeature` (exposed on `IPermissionDefinitionManager`).
3. Enable per tenant with `IFeatureValueManager.SetAsync("Sales", "true", "T", tenantId)`.

The [`GET /api/app-config`](../src/Slice.AspNetCore.AppConfig/README.md) endpoint
(`Slice.AspNetCore.AppConfig`) then returns, per user/tenant, the **feature-filtered** granted permissions
+ enabled features + a permission/feature-filtered **menu** (`IMenuContributor`) + current user/tenant +
client-visible settings — so a tenant without the Sales feature never sees its permissions or nav. (The
`[RequiresFeature]`/`[SlicePermission]` pipeline gates still enforce access regardless; this is the
visibility/composition layer.)

---

## Localization

`Slice.Localization` — contributor-based string localization. ([README](../src/Slice.Localization/README.md))

Implement `ILocalizationContributor` per culture (key → string), resolve via `ISliceLocalizer`:

```csharp
app.UseSliceLocalization();   // derives supported cultures from the registered contributors

var text = localizer["Lead.Created"];          // current culture → parent → default("en") → key
var msg  = localizer.Format("Greeting", name); // formatted
```

Resolution falls back: current culture → its two-letter parent → the default culture (`en`) → the key
itself.

---

## Caching

`Slice.Caching` — `ISliceCache` over `IDistributedCache` with JSON serialization and **tenant-isolated
keys** (`t:{tenantId}:{key}` or `host:{key}`). ([README](../src/Slice.Caching/README.md))

```csharp
var dto = await cache.GetOrAddAsync(
    key: $"lead:{id}",
    factory: async ct => await LoadAsync(id, ct),
    ttl: TimeSpan.FromMinutes(5));   // default TTL is 10 minutes
```

Default backend is the in-memory distributed cache. Swap to Redis:

```csharp
services.AddSliceRedisCache("localhost:6379", instanceName: "slice:");   // Slice.Caching.Redis
```

> `GetOrAddAsync<T>` for **value types** checks the raw cached bytes for presence before deserializing,
> so a cached `0`/`false` is not mistaken for "missing".

---

## Blob storage

`Slice.BlobStoring` — `IBlobProvider` backend + typed `IBlobContainer<TContainer>` markers.
([README](../src/Slice.BlobStoring/README.md))

```csharp
[BlobContainerName("lead-docs")]                  // else: class name minus "Container", lowercased
public sealed class LeadDocumentsContainer { }

public sealed class UploadHandler(IBlobContainer<LeadDocumentsContainer> container)
{
    public Task Save(string name, Stream s, CancellationToken ct)
        => container.SaveAsync(name, s, overrideExisting: true, ct);
}
```

Default backend is the file system (`BlobStoring:FileSystem:BasePath`). Swap the backend:

| Backend | Registration | Package |
|---|---|---|
| AWS S3 | `AddSliceBlobStoringAws(bucket, client?)` | `Slice.BlobStoring.Aws` |
| Azure Blob | `AddSliceBlobStoringAzure(connectionString)` | `Slice.BlobStoring.Azure` |
| MinIO | `AddSliceBlobStoringMinio(o => { o.Endpoint = …; o.Bucket = …; })` | `Slice.BlobStoring.Minio` |

S3/MinIO key blobs as `{container}/{blob}` in one bucket; Azure maps each Slice container to an Azure
container. (MinIO verified against a real container.)

---

## Email

`Slice.Emailing` — `IEmailSender` + `EmailMessage(To, Subject, Body, IsHtml, From)`. Default is
`NullEmailSender` (logs). ([README](../src/Slice.Emailing/README.md))

```csharp
await emailSender.SendAsync(new EmailMessage("a@x.com", "Hello", "<b>hi</b>", IsHtml: true));
```

Backends:

```csharp
services.AddSliceSmtpEmailSender(o => { o.Host = "smtp.example.com"; o.Port = 587; });   // Slice.Emailing
services.AddSliceMailKit(o => { o.Host = "smtp.example.com"; o.Port = 587; o.Security = SecureSocketOptions.StartTls; }); // Slice.Emailing.MailKit
```

MailKit adds **attachments + multiple recipients** (comma/semicolon-separated `To`); inject the
concrete `MailKitEmailSender` for the attachment overload. (Verified against a real Mailpit SMTP
container.)

---

## Background jobs

`Slice.BackgroundJobs` — fire-and-forget + recurring jobs. ([README](../src/Slice.BackgroundJobs/README.md))

```csharp
public sealed record SendDigestArgs(Guid UserId);

public sealed class SendDigestJob(IEmailSender email) : IBackgroundJob<SendDigestArgs>
{
    public Task ExecuteAsync(SendDigestArgs args, CancellationToken ct) => /* … */;
}

// enqueue (default manager: in-memory channel + a hosted worker)
await jobManager.EnqueueAsync(new SendDigestArgs(userId), delay: TimeSpan.FromMinutes(5));
```

Register handlers with `AddBackgroundJobHandlers(assembly)`. For durable jobs, swap to Hangfire:

```csharp
services.AddSliceHangfire(cfg => cfg.UseInMemoryStorage());   // Slice.BackgroundJobs.Hangfire
```

Recurring jobs use `IRecurringJobManager.AddOrUpdate<TArgs>(...)`.

---

## Background workers

`Slice.BackgroundWorkers` — periodic workers. ([README](../src/Slice.BackgroundWorkers/README.md))

```csharp
public sealed class StaleLeadSweeper : IBackgroundWorker
{
    public TimeSpan Period => TimeSpan.FromMinutes(15);
    public async Task DoWorkAsync(IServiceProvider services, CancellationToken ct) { /* … */ }
}
```

Workers carrying a DI marker are discovered and run by the worker manager on their `Period`.

---

## Distributed locking

`IDistributedLock` (the seam lives in `Slice.Core`; default `NullDistributedLock`).
`Slice.DistributedLocking` provides an in-process lock; `Slice.DistributedLocking.Redis` a multi-node
one. ([README](../src/Slice.DistributedLocking/README.md))

```csharp
await using var handle = await distributedLock.TryAcquireAsync("outbox:crm", TimeSpan.FromSeconds(30), ct);
if (handle is not null) { /* exclusive section */ }
```

```csharp
services.AddSliceRedisDistributedLock("localhost:6379");   // Redis: SET NX PX + compare-and-delete
```

The outbox processor uses a distributed lock so only one node publishes at a time.

---

## Object mapping

`Slice.ObjectMapping` — `IObjectMapper` resolves typed `IObjectMapper<TSource, TDestination>` mappers.
([README](../src/Slice.ObjectMapping/README.md))

```csharp
public sealed class LeadToDto : IObjectMapper<Lead, LeadDto>   // hand-written or Mapperly/AutoMapper-generated
{
    public LeadDto Map(Lead s) => new(s.Id, s.Name.FirstName, s.Name.LastName);
}

var dto = objectMapper.Map<Lead, LeadDto>(lead);
```

Mappers carrying a marker are registered by convention; a Mapperly-generated class implementing
`IObjectMapper<,>` slots in identically.

---

## API versioning

`Slice.ApiVersioning` wraps Asp.Versioning. ([README](../src/Slice.ApiVersioning/README.md))

```csharp
builder.Services.AddSliceApiVersioning();
// DefaultApiVersion 1.0, AssumeDefaultVersionWhenUnspecified, ReportApiVersions,
// version read from URL segment OR the X-Api-Version header
```

Responses include the `api-supported-versions` header; clients can request a version via URL segment
or header.

---

## Virtual file system

`Slice.VirtualFileSystem` — a composite file provider over **embedded resources + physical files**.
([README](../src/Slice.VirtualFileSystem/README.md))

```csharp
services.ConfigureVirtualFileSystem(vfs => vfs.AddEmbedded<CrmModule>());   // embedded resources from this assembly

var template = await virtualFileProvider.ReadAsStringAsync("/Resources/welcome.txt");   // null if missing
```

Useful for bundling templates/assets in a library and overriding them with physical files in the host.

---

## Serilog

`Slice.Serilog` — Serilog as the logging provider + request logging with tenant/user enrichment.
([README](../src/Slice.Serilog/README.md))

```csharp
builder.UseSliceSerilog(lc => lc.WriteTo.Console());   // builds Log.Logger, ClearProviders, AddSerilog

app.UseSliceSerilogRequestLogging();   // pushes TenantId/UserId into LogContext, then request logging
```

Every log written during a request carries `TenantId` and `UserId` (from `ICurrentTenant`/`ICurrentUser`).

---

## SignalR

`Slice.AspNetCore.SignalR` — real-time hubs that are tenant/user-aware from the connection principal.
([README](../src/Slice.AspNetCore.SignalR/README.md))

```csharp
public sealed class NotificationsHub : SliceHub
{
    public Task Subscribe() => Groups.AddToGroupAsync(Context.ConnectionId, $"tenant:{CurrentTenantId}");
    // CurrentUserId (sub/NameIdentifier), CurrentTenantId (tenant_id), CurrentUserName, CurrentPrincipal
}

// composition: [DependsOn(typeof(SliceSignalRModule))] or services.AddSliceSignalR();
app.MapSliceHub<NotificationsHub>("/hubs/notifications");
```
