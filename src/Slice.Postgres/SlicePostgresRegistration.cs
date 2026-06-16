using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Slice.Postgres;

public static class SlicePostgresRegistration
{
    /// <summary>
    /// Registers a shared <see cref="NpgsqlDataSource"/> (one connection pool reused by every Slice
    /// Postgres adapter) and the schema initializer. Safe to call multiple times — the data source is
    /// added with <c>TryAddSingleton</c> (first caller wins) and the initializer is de-duplicated, so
    /// the whole stack shares a single pool.
    /// </summary>
    /// <param name="configure">
    /// Optional hook on the <see cref="NpgsqlDataSourceBuilder"/> — e.g. <c>b => b.UseVector()</c> for
    /// pgvector. Only applied when this call is the one that builds the data source, so when composing
    /// the stack pass it on the first/controlling registration (or use <c>AddSlicePostgresStack</c>).
    /// </param>
    public static IServiceCollection AddSlicePostgres(
        this IServiceCollection services, string connectionString, Action<NpgsqlDataSourceBuilder>? configure = null)
    {
        services.TryAddSingleton<NpgsqlDataSource>(_ =>
        {
            var builder = new NpgsqlDataSourceBuilder(connectionString);
            configure?.Invoke(builder);
            return builder.Build();
        });

        // TryAddEnumerable inside AddHostedService de-duplicates by implementation type.
        services.AddHostedService<PostgresSchemaInitializer>();
        return services;
    }

    /// <summary>Registers an adapter's idempotent DDL so the initializer creates its objects at startup.</summary>
    public static IServiceCollection AddPostgresSchema(this IServiceCollection services, string ddl)
    {
        services.AddSingleton<IPostgresSchema>(new InlinePostgresSchema(ddl));
        return services;
    }

    private sealed class InlinePostgresSchema(string ddl) : IPostgresSchema
    {
        public string Ddl { get; } = ddl;
    }
}
