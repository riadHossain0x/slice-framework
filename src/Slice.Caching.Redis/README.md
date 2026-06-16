# Slice.Caching.Redis

> Swap the default in-memory distributed cache for StackExchange Redis without changing any `ISliceCache` consumer.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

Slice.Caching.Redis is a thin adapter package. Its single extension, `AddSliceRedisCache(...)`, removes the default in-memory `IDistributedCache` registered by `Slice.Caching` and replaces it with the StackExchange Redis implementation. Because `ISliceCache` / `SliceCache` are built on `IDistributedCache`, they continue to work unchanged on top of Redis.

## Dependencies

- **Slice:** `Slice.Caching` (`ISliceCache`, `SliceCache`, `SliceCachingModule`)
- **Third-party:** `Microsoft.Extensions.Caching.StackExchangeRedis`, `Microsoft.Extensions.DependencyInjection.Abstractions`

## Module & registration

This package ships no `SliceModule` — it is a single `IServiceCollection` extension, called after the caching module has registered its default cache.

```csharp
services.AddSliceRedisCache(
    connectionString: "localhost:6379",
    instanceName: "myapp:"); // optional key prefix
```

## Key types

| Type | Kind | Description |
|---|---|---|
| `RedisCacheRegistration` | static class | Hosts the `AddSliceRedisCache` extension. |
| `AddSliceRedisCache` | extension method | `IServiceCollection AddSliceRedisCache(this IServiceCollection services, string connectionString, string? instanceName = null)`. Removes the existing `IDistributedCache` and adds the StackExchange Redis cache. |

## Usage

```csharp
var builder = WebApplication.CreateBuilder(args);

// SliceCachingModule registers AddDistributedMemoryCache() by default...
// ...then replace it with Redis:
builder.Services.AddSliceRedisCache(
    builder.Configuration.GetConnectionString("Redis")!,
    instanceName: "myapp:");

// ISliceCache consumers need no changes.
```

## Notes

- **Replacement semantics.** The extension calls `services.RemoveAll<IDistributedCache>()` before `AddStackExchangeRedisCache(...)`, so the in-memory default from `SliceCachingModule` is fully removed. Call it after the caching module is registered.
- **Options.** `connectionString` maps to `RedisCacheOptions.Configuration`. `instanceName` (optional) maps to `RedisCacheOptions.InstanceName` and is only set when non-null.
- **No behavior change to `ISliceCache`.** Tenant key normalization, JSON serialization, and default TTL all come from `SliceCache` and are unaffected by switching the backing store.
