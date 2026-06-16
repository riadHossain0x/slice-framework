using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Slice.EntityFrameworkCore.PostgreSQL;

public static class SlicePostgresEfExtensions
{
    /// <summary>
    /// Configures a <see cref="SliceDbContext"/> to use PostgreSQL via the shared
    /// <see cref="NpgsqlDataSource"/>, so EF data, the transactional outbox, the inbox, auth and
    /// management all run on Postgres reusing the stack's single connection pool. Resolve the data
    /// source from DI inside <c>AddSliceDbContext&lt;T&gt;((sp, o) =&gt; o.UseSlicePostgres(sp.GetRequiredService&lt;NpgsqlDataSource&gt;()))</c>.
    /// </summary>
    public static DbContextOptionsBuilder UseSlicePostgres(
        this DbContextOptionsBuilder options, NpgsqlDataSource dataSource)
        => options.UseNpgsql(dataSource);

    /// <summary>
    /// Configures a <see cref="SliceDbContext"/> to use PostgreSQL from a connection string. Prefer the
    /// <see cref="NpgsqlDataSource"/> overload when composing the Postgres stack so EF shares its pool.
    /// </summary>
    public static DbContextOptionsBuilder UseSlicePostgres(
        this DbContextOptionsBuilder options, string connectionString)
        => options.UseNpgsql(connectionString);
}
