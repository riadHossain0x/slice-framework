using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Slice.Core.Ambient;
using Slice.Postgres;

namespace Slice.DistributedLocking.Postgres;

/// <summary>
/// A cross-node <see cref="IDistributedLock"/> using PostgreSQL <b>session-level advisory locks</b>.
/// The lock key is hashed to a bigint (<c>hashtextextended</c>) and held on a dedicated connection for
/// the lifetime of the returned handle; disposing the handle unlocks and returns the connection. A
/// crashed holder's lock is released automatically when its session ends — no TTL needed.
/// </summary>
public sealed class PostgresDistributedLock(NpgsqlDataSource dataSource) : IDistributedLock
{
    private static readonly TimeSpan PollDelay = TimeSpan.FromMilliseconds(50);

    public async Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.Zero);
        var connection = await dataSource.OpenConnectionAsync(ct);
        try
        {
            while (true)
            {
                if (await TryLockAsync(connection, key, ct))
                    return new Handle(connection, key);

                if (DateTime.UtcNow >= deadline)
                {
                    await connection.DisposeAsync();
                    return null;
                }
                await Task.Delay(PollDelay, ct);
            }
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task<bool> TryLockAsync(NpgsqlConnection connection, string key, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT pg_try_advisory_lock(hashtextextended(@k, 0))";
        cmd.Parameters.AddWithValue("k", key);
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }

    private sealed class Handle(NpgsqlConnection connection, string key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT pg_advisory_unlock(hashtextextended(@k, 0))";
                cmd.Parameters.AddWithValue("k", key);
                await cmd.ExecuteScalarAsync();
            }
            finally
            {
                await connection.DisposeAsync();   // returns the session to the pool, releasing any held locks
            }
        }
    }
}

public static class PostgresDistributedLockRegistration
{
    /// <summary>Replaces the distributed lock with PostgreSQL advisory locks. Pass a connection string to
    /// register the shared data source here, or omit it when it is already registered.</summary>
    public static IServiceCollection AddSlicePostgresDistributedLock(
        this IServiceCollection services, string? connectionString = null)
    {
        if (connectionString is not null)
            services.AddSlicePostgres(connectionString);

        services.RemoveAll<IDistributedLock>();
        services.AddSingleton<IDistributedLock, PostgresDistributedLock>();
        return services;
    }
}
