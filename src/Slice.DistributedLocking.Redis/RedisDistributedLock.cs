using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Slice.Core.Ambient;
using StackExchange.Redis;

namespace Slice.DistributedLocking.Redis;

/// <summary>
/// Redis-backed lock using <c>SET key token NX PX ttl</c> for acquisition and a compare-and-delete
/// Lua script for release (so a holder only releases its own lock).
/// </summary>
public sealed class RedisDistributedLock(IConnectionMultiplexer redis) : IDistributedLock
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);
    private const string ReleaseScript =
        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";

    public async Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var token = Guid.NewGuid().ToString("N");
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.Zero);

        do
        {
            if (await db.StringSetAsync(key, token, Ttl, When.NotExists))
                return new Releaser(db, key, token);
            if (DateTime.UtcNow < deadline)
                await Task.Delay(50, ct);
        }
        while (DateTime.UtcNow < deadline);

        return null;
    }

    private sealed class Releaser(IDatabase db, string key, string token) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
            => await db.ScriptEvaluateAsync(ReleaseScript, [key], [token]);
    }
}

public static class RedisDistributedLockRegistration
{
    public static IServiceCollection AddSliceRedisDistributedLock(this IServiceCollection services, string connectionString)
    {
        services.RemoveAll<IDistributedLock>();
        services.TryAddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(connectionString));
        services.AddSingleton<IDistributedLock, RedisDistributedLock>();
        return services;
    }
}
