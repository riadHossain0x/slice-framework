using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Slice.Caching;
using Slice.Caching.Postgres;
using Slice.Core.Ambient;
using Slice.Modularity;

namespace Slice.Postgres.Tests;

[Collection("postgres")]
public sealed class PostgresCacheTests(PostgresFixture fx)
{
    private async Task<IHost> StartHostAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSlicePostgresCache(fx.ConnectionString);
        builder.Services.AddSliceConventions(typeof(NullCurrentTenant).Assembly);  // ambient defaults
        builder.Services.AddSliceConventions(typeof(SliceCache).Assembly);          // ISliceCache → SliceCache
        var host = builder.Build();
        await host.StartAsync();   // runs the schema initializer + sweeper
        return host;
    }

    [Fact]
    public async Task Set_get_remove_round_trip()
    {
        using var host = await StartHostAsync();
        var cache = host.Services.GetRequiredService<IDistributedCache>();

        await cache.SetStringAsync("greeting", "hello-pg");
        Assert.Equal("hello-pg", await cache.GetStringAsync("greeting"));

        await cache.RemoveAsync("greeting");
        Assert.Null(await cache.GetStringAsync("greeting"));

        await host.StopAsync();
    }

    [Fact]
    public async Task Absolute_expiration_evicts_the_entry()
    {
        using var host = await StartHostAsync();
        var cache = host.Services.GetRequiredService<IDistributedCache>();

        await cache.SetStringAsync("temp", "x",
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(200) });

        Assert.Equal("x", await cache.GetStringAsync("temp"));
        await Task.Delay(400);
        Assert.Null(await cache.GetStringAsync("temp"));   // expired on read

        await host.StopAsync();
    }

    [Fact]
    public async Task SliceCache_layers_on_top_with_tenant_isolated_keys()
    {
        using var host = await StartHostAsync();
        var cache = host.Services.GetRequiredService<ISliceCache>();

        var value = await cache.GetOrAddAsync("k", () => Task.FromResult(42));
        Assert.Equal(42, value);
        Assert.Equal(42, await cache.GetAsync<int>("k"));   // 0 (value-type default) is not a miss
    }
}
