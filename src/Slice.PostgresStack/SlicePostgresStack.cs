using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pgvector;
using Slice.BackgroundJobs.Postgres;
using Slice.BlobStoring.Postgres;
using Slice.Caching.Postgres;
using Slice.DistributedLocking.Postgres;
using Slice.EventBus.Postgres;
using Slice.Postgres;
using Slice.Vector.Postgres;

namespace Slice.PostgresStack;

/// <summary>Toggles for which Postgres-backed adapters the stack wires up (all on by default).</summary>
public sealed class PostgresStackOptions
{
    public bool Cache { get; set; } = true;
    public bool DistributedLock { get; set; } = true;
    public bool EventBus { get; set; } = true;
    public bool BackgroundJobs { get; set; } = true;
    public bool BlobStoring { get; set; } = true;
    public bool VectorStore { get; set; } = true;
}

public static class SlicePostgresStackRegistration
{
    /// <summary>
    /// Wires the whole Postgres stack onto a <b>single shared connection pool</b>: caching, distributed
    /// locking, the distributed event bus (LISTEN/NOTIFY), durable background jobs, blob storage and the
    /// pgvector store — each swapping its framework default. Pair with
    /// <c>AddSliceDbContext&lt;T&gt;((sp, o) =&gt; o.UseSlicePostgres(sp.GetRequiredService&lt;NpgsqlDataSource&gt;()))</c>
    /// so EF data, the outbox/inbox, auth and management run on the same Postgres too.
    /// </summary>
    public static IServiceCollection AddSlicePostgresStack(
        this IServiceCollection services, string connectionString, Action<PostgresStackOptions>? configure = null)
    {
        var options = new PostgresStackOptions();
        configure?.Invoke(options);

        // Build the one shared NpgsqlDataSource up front (with pgvector mapping when the vector store is on).
        services.AddSlicePostgres(connectionString, builder =>
        {
            if (options.VectorStore) builder.UseVector();
        });

        // Each adapter reuses the shared data source (no connection string ⇒ no re-registration).
        if (options.Cache) services.AddSlicePostgresCache();
        if (options.DistributedLock) services.AddSlicePostgresDistributedLock();
        if (options.EventBus) services.AddSlicePostgresEventBus();
        if (options.BackgroundJobs) services.AddSlicePostgresBackgroundJobs();
        if (options.BlobStoring) services.AddSlicePostgresBlobStoring();
        if (options.VectorStore) services.AddSlicePostgresVectorStore();

        return services;
    }
}
