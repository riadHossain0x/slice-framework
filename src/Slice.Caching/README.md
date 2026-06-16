# Slice.Caching

> A typed, tenant-aware cache abstraction over `IDistributedCache` with JSON serialization and get-or-add semantics.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

Slice.Caching wraps `IDistributedCache` in a small, typed interface (`ISliceCache`). Values are serialized as JSON (web defaults) and keys are normalized with the current tenant so one tenant never reads another's entry. The module registers a default in-memory distributed cache, which `Slice.Caching.Redis` can swap for Redis without changing any consuming code.

## Dependencies

- **Slice:** `Slice.Core` (`ICurrentTenant`, DI markers), `Slice.Modularity`, `Slice.Application` (`SliceApplicationModule`)
- **Third-party:** `Microsoft.Extensions.Caching.Abstractions` (`IDistributedCache`), `Microsoft.Extensions.Caching.Memory`, `Microsoft.Extensions.DependencyInjection.Abstractions`

## Module & registration

```csharp
[DependsOn(typeof(SliceApplicationModule))]
public sealed class SliceCachingModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddDistributedMemoryCache(); // default; replaced by the Redis adapter
        context.Services.AddSliceConventions(typeof(SliceCachingModule).Assembly);
    }
}
```

The module registers a default in-memory `IDistributedCache` and discovers `SliceCache` by convention. To use Redis instead, call `AddSliceRedisCache(...)` from `Slice.Caching.Redis`.

## Key types

| Type | Kind | Description |
|---|---|---|
| `ISliceCache` | interface | Typed cache contract (all methods take an optional `CancellationToken ct = default`). |
| `SliceCache` | sealed (`ITransientDependency`) | Implementation over `IDistributedCache` + `ICurrentTenant`; JSON (web) serialization, default 10-minute TTL, tenant-isolated keys. |

### `ISliceCache` members

| Member | Description |
|---|---|
| `Task<T?> GetAsync<T>(string key, …)` | Reads and deserializes; returns `default` on miss. |
| `Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, …)` | Serializes and stores with absolute-relative expiration (`ttl` or default). |
| `Task<T> GetOrAddAsync<T>(string key, Func<Task<T>> factory, TimeSpan? ttl = null, …)` | Returns cached value if present; otherwise invokes `factory`, stores, and returns it. |
| `Task RemoveAsync(string key, …)` | Removes the entry. |

## Usage

```csharp
public sealed class CatalogReadService(ISliceCache cache, ICatalogRepository repo)
{
    public Task<CatalogDto?> GetAsync(Guid id) =>
        cache.GetOrAddAsync(
            key: $"catalog:{id}",
            factory: () => repo.LoadAsync(id),
            ttl: TimeSpan.FromMinutes(30));

    public async Task RefreshAsync(Guid id)
    {
        await cache.RemoveAsync($"catalog:{id}");
        var fresh = await repo.LoadAsync(id);
        await cache.SetAsync($"catalog:{id}", fresh); // uses default 10-minute TTL
    }
}
```

## Notes

- **Tenant isolation.** Keys are normalized to `t:{tenantId}:{key}` when a current tenant is present, else `host:{key}` — entries are never shared across tenants.
- **Default TTL.** `SetAsync` (and `GetOrAddAsync` when it stores) uses `AbsoluteExpirationRelativeToNow`; the default is 10 minutes when `ttl` is omitted.
- **Value-type presence gotcha.** `GetOrAddAsync` checks raw byte presence from `IDistributedCache` (not the deserialized result) before deciding a miss. This is deliberate: a cached value-type default such as `0` or `false` must not be mistaken for "not cached". The deserialized value is returned with `!` (non-null assertion) on a hit.
- **Serialization.** `JsonSerializer` with `JsonSerializerDefaults.Web`. Cached types must round-trip through this.
- **Lifetime.** `SliceCache` is transient; the underlying `IDistributedCache` lifetime is whatever the registered provider uses (in-memory by default).
