using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Slice.BlobStoring;
using Slice.Postgres;

namespace Slice.BlobStoring.Postgres;

/// <summary>Stores blobs as <c>bytea</c> rows in a single Postgres table (<c>slice_blobs</c>), keyed by
/// (container, name). Keeps binary content in the same database as everything else.</summary>
public sealed class PostgresBlobProvider(NpgsqlDataSource dataSource) : IBlobProvider
{
    internal const string Ddl = """
        CREATE TABLE IF NOT EXISTS slice_blobs (
            container text NOT NULL,
            name      text NOT NULL,
            data      bytea NOT NULL,
            PRIMARY KEY (container, name)
        );
        """;

    public async Task SaveAsync(string container, string blob, Stream stream, bool overrideExisting, CancellationToken ct)
    {
        if (!overrideExisting && await ExistsAsync(container, blob, ct))
            throw new InvalidOperationException($"Blob '{blob}' already exists in '{container}'.");

        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO slice_blobs (container, name, data) VALUES (@c, @n, @d)
            ON CONFLICT (container, name) DO UPDATE SET data = EXCLUDED.data
            """;
        cmd.Parameters.AddWithValue("c", container);
        cmd.Parameters.AddWithValue("n", blob);
        cmd.Parameters.AddWithValue("d", ms.ToArray());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<Stream?> GetOrNullAsync(string container, string blob, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM slice_blobs WHERE container = @c AND name = @n";
        cmd.Parameters.AddWithValue("c", container);
        cmd.Parameters.AddWithValue("n", blob);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is byte[] bytes ? new MemoryStream(bytes) : null;
    }

    public async Task<bool> ExistsAsync(string container, string blob, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM slice_blobs WHERE container = @c AND name = @n";
        cmd.Parameters.AddWithValue("c", container);
        cmd.Parameters.AddWithValue("n", blob);
        return await cmd.ExecuteScalarAsync(ct) is not null;
    }

    public async Task<bool> DeleteAsync(string container, string blob, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM slice_blobs WHERE container = @c AND name = @n";
        cmd.Parameters.AddWithValue("c", container);
        cmd.Parameters.AddWithValue("n", blob);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }
}

public static class PostgresBlobStoringRegistration
{
    /// <summary>Uses Postgres (<c>slice_blobs</c>) as the blob backend. Pass a connection string to
    /// register the shared data source here.</summary>
    public static IServiceCollection AddSlicePostgresBlobStoring(
        this IServiceCollection services, string? connectionString = null)
    {
        if (connectionString is not null)
            services.AddSlicePostgres(connectionString);

        services.AddPostgresSchema(PostgresBlobProvider.Ddl);
        services.RemoveAll<IBlobProvider>();
        services.AddSingleton<IBlobProvider, PostgresBlobProvider>();
        return services;
    }
}
