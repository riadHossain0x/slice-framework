using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Slice.Caching.Redis;

public static class RedisCacheRegistration
{
    /// <summary>
    /// Replaces the default in-memory distributed cache with Redis (StackExchange).
    /// <see cref="ISliceCache"/> works unchanged on top of it.
    /// </summary>
    public static IServiceCollection AddSliceRedisCache(
        this IServiceCollection services, string connectionString, string? instanceName = null)
    {
        services.RemoveAll<IDistributedCache>();
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = connectionString;
            if (instanceName is not null)
                options.InstanceName = instanceName;
        });
        return services;
    }
}
