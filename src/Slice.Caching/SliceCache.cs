using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Slice.Core.Ambient;
using Slice.Core.DependencyInjection;

namespace Slice.Caching;

/// <summary>Typed, tenant-aware cache over <see cref="IDistributedCache"/>.</summary>
public interface ISliceCache
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default);
    Task<T> GetOrAddAsync<T>(string key, Func<Task<T>> factory, TimeSpan? ttl = null, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
}

public sealed class SliceCache(IDistributedCache cache, ICurrentTenant currentTenant) : ISliceCache, ITransientDependency
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        var bytes = await cache.GetAsync(Normalize(key), ct);
        return bytes is null ? default : JsonSerializer.Deserialize<T>(bytes, Json);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        var options = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl ?? DefaultTtl };
        return cache.SetAsync(Normalize(key), bytes, options, ct);
    }

    public async Task<T> GetOrAddAsync<T>(string key, Func<Task<T>> factory, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        // Check raw presence (so cached value-type defaults like 0/false aren't mistaken for a miss).
        var bytes = await cache.GetAsync(Normalize(key), ct);
        if (bytes is not null)
            return JsonSerializer.Deserialize<T>(bytes, Json)!;

        var value = await factory();
        await SetAsync(key, value, ttl, ct);
        return value;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default) => cache.RemoveAsync(Normalize(key), ct);

    // Tenant-isolated keys so one tenant never reads another's cache entry.
    private string Normalize(string key)
        => currentTenant.Id is { } tenantId ? $"t:{tenantId}:{key}" : $"host:{key}";
}
