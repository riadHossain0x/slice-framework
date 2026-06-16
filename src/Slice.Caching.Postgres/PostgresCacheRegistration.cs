using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Slice.Postgres;

namespace Slice.Caching.Postgres;

/// <summary>Periodically deletes expired cache rows so the table doesn't grow unbounded.</summary>
public sealed class PostgresCacheSweeper(NpgsqlDataSource dataSource) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = await dataSource.OpenConnectionAsync(stoppingToken);
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM slice_cache WHERE expires_at IS NOT NULL AND expires_at <= now()";
                await cmd.ExecuteNonQueryAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch { /* best-effort sweep; try again next tick */ }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}

public static class PostgresCacheRegistration
{
    /// <summary>
    /// Replaces the distributed cache with a Postgres-backed one. Pass a connection string to register
    /// the shared <see cref="NpgsqlDataSource"/> here, or omit it when the data source is already
    /// registered (e.g. via <c>AddSlicePostgres</c> or <c>AddSlicePostgresStack</c>).
    /// </summary>
    public static IServiceCollection AddSlicePostgresCache(
        this IServiceCollection services, string? connectionString = null)
    {
        if (connectionString is not null)
            services.AddSlicePostgres(connectionString);

        services.AddPostgresSchema(PostgresDistributedCache.Ddl);
        services.RemoveAll<IDistributedCache>();
        services.AddSingleton<IDistributedCache, PostgresDistributedCache>();
        services.AddHostedService<PostgresCacheSweeper>();
        return services;
    }
}
