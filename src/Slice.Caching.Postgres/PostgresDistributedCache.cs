using Microsoft.Extensions.Caching.Distributed;
using Npgsql;

namespace Slice.Caching.Postgres;

/// <summary>
/// An <see cref="IDistributedCache"/> backed by a single Postgres table (<c>slice_cache</c>). Absolute
/// and sliding expirations are honoured; a background sweeper removes expired rows. <c>ISliceCache</c>
/// (typed, tenant-aware) works unchanged on top of it.
/// </summary>
public sealed class PostgresDistributedCache(NpgsqlDataSource dataSource) : IDistributedCache
{
    internal const string Ddl = """
        CREATE TABLE IF NOT EXISTS slice_cache (
            key                 text PRIMARY KEY,
            value               bytea NOT NULL,
            expires_at          timestamptz NULL,
            sliding_seconds     double precision NULL,
            absolute_expires_at timestamptz NULL
        );
        CREATE INDEX IF NOT EXISTS ix_slice_cache_expires ON slice_cache (expires_at);
        """;

    public byte[]? Get(string key) => GetAsync(key).GetAwaiter().GetResult();

    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(token);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT value, expires_at, sliding_seconds, absolute_expires_at
            FROM slice_cache WHERE key = @k
            """;
        cmd.Parameters.AddWithValue("k", key);

        await using var reader = await cmd.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token))
            return null;

        var value = (byte[])reader["value"];
        var expiresAt = reader["expires_at"] as DateTime?;
        var sliding = reader["sliding_seconds"] as double?;
        var absolute = reader["absolute_expires_at"] as DateTime?;
        await reader.CloseAsync();

        var now = DateTime.UtcNow;
        if (expiresAt is { } exp && exp <= now)
        {
            await RemoveAsync(key, token);
            return null;
        }

        // Sliding expiration: extend the window on access (capped by the absolute expiry).
        if (sliding is { } slide)
        {
            var next = now.AddSeconds(slide);
            if (absolute is { } abs && next > abs) next = abs;
            await using var slideCmd = connection.CreateCommand();
            slideCmd.CommandText = "UPDATE slice_cache SET expires_at = @e WHERE key = @k";
            slideCmd.Parameters.AddWithValue("e", next);
            slideCmd.Parameters.AddWithValue("k", key);
            await slideCmd.ExecuteNonQueryAsync(token);
        }

        return value;
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        => SetAsync(key, value, options).GetAwaiter().GetResult();

    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        var now = DateTime.UtcNow;
        DateTime? absolute = options.AbsoluteExpiration?.UtcDateTime
            ?? (options.AbsoluteExpirationRelativeToNow is { } rel ? now.Add(rel) : null);
        double? slidingSeconds = options.SlidingExpiration?.TotalSeconds;
        DateTime? expiresAt = slidingSeconds is { } s ? now.AddSeconds(s) : absolute;
        if (expiresAt is { } e && absolute is { } a && e > a) expiresAt = a;

        await using var connection = await dataSource.OpenConnectionAsync(token);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO slice_cache (key, value, expires_at, sliding_seconds, absolute_expires_at)
            VALUES (@k, @v, @e, @s, @a)
            ON CONFLICT (key) DO UPDATE
              SET value = EXCLUDED.value,
                  expires_at = EXCLUDED.expires_at,
                  sliding_seconds = EXCLUDED.sliding_seconds,
                  absolute_expires_at = EXCLUDED.absolute_expires_at
            """;
        cmd.Parameters.AddWithValue("k", key);
        cmd.Parameters.AddWithValue("v", value);
        cmd.Parameters.AddWithValue("e", (object?)expiresAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("s", (object?)slidingSeconds ?? DBNull.Value);
        cmd.Parameters.AddWithValue("a", (object?)absolute ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(token);
    }

    public void Refresh(string key) => RefreshAsync(key).GetAwaiter().GetResult();

    // GetAsync already slides on access; a no-value refresh is the same read path.
    public Task RefreshAsync(string key, CancellationToken token = default) => GetAsync(key, token);

    public void Remove(string key) => RemoveAsync(key).GetAwaiter().GetResult();

    public async Task RemoveAsync(string key, CancellationToken token = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(token);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM slice_cache WHERE key = @k";
        cmd.Parameters.AddWithValue("k", key);
        await cmd.ExecuteNonQueryAsync(token);
    }
}
