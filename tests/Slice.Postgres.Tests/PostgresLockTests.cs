using Npgsql;
using Slice.DistributedLocking.Postgres;

namespace Slice.Postgres.Tests;

[Collection("postgres")]
public sealed class PostgresLockTests(PostgresFixture fx)
{
    [Fact]
    public async Task Lock_is_exclusive_and_released_on_dispose()
    {
        await using var dataSource = NpgsqlDataSource.Create(fx.ConnectionString);
        var lockProvider = new PostgresDistributedLock(dataSource);

        // First holder acquires the key.
        var first = await lockProvider.TryAcquireAsync("orders", TimeSpan.FromSeconds(1));
        Assert.NotNull(first);

        // A second attempt (different session) cannot acquire while it is held.
        var blocked = await lockProvider.TryAcquireAsync("orders", TimeSpan.FromMilliseconds(200));
        Assert.Null(blocked);

        // A different key is independent.
        var other = await lockProvider.TryAcquireAsync("shipments", TimeSpan.FromMilliseconds(200));
        Assert.NotNull(other);
        await other!.DisposeAsync();

        // After releasing, the key can be acquired again.
        await first!.DisposeAsync();
        var reacquired = await lockProvider.TryAcquireAsync("orders", TimeSpan.FromSeconds(1));
        Assert.NotNull(reacquired);
        await reacquired!.DisposeAsync();
    }
}
