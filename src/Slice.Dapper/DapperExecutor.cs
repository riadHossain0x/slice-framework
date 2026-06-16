using Dapper;
using Slice.EntityFrameworkCore;

namespace Slice.Dapper;

/// <summary>
/// Runs Dapper queries on a specific <see cref="SliceDbContext"/>'s ADO.NET connection and ambient
/// transaction, so raw-SQL reads/writes participate in the same unit of work as EF Core changes.
/// Inject <c>IDapperExecutor&lt;TContext&gt;</c> where <c>TContext</c> is your bounded-context DbContext.
/// </summary>
public interface IDapperExecutor<TContext> where TContext : SliceDbContext
{
    Task<IEnumerable<T>> QueryAsync<T>(string sql, object? param = null, CancellationToken ct = default);
    Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? param = null, CancellationToken ct = default);
    Task<T> QuerySingleAsync<T>(string sql, object? param = null, CancellationToken ct = default);
    Task<int> ExecuteAsync(string sql, object? param = null, CancellationToken ct = default);
    Task<T?> ExecuteScalarAsync<T>(string sql, object? param = null, CancellationToken ct = default);
}

public sealed class DapperExecutor<TContext>(TContext db) : IDapperExecutor<TContext>
    where TContext : SliceDbContext
{
    private async Task<CommandDefinition> CommandAsync(string sql, object? param, CancellationToken ct)
    {
        await db.GetOpenConnectionAsync(ct);
        // Enlist in the context's ambient transaction (null when not inside one).
        return new CommandDefinition(sql, param, transaction: db.GetCurrentTransaction(), cancellationToken: ct);
    }

    public async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? param = null, CancellationToken ct = default)
        => await db.GetDbConnection().QueryAsync<T>(await CommandAsync(sql, param, ct));

    public async Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? param = null, CancellationToken ct = default)
        => await db.GetDbConnection().QueryFirstOrDefaultAsync<T>(await CommandAsync(sql, param, ct));

    public async Task<T> QuerySingleAsync<T>(string sql, object? param = null, CancellationToken ct = default)
        => await db.GetDbConnection().QuerySingleAsync<T>(await CommandAsync(sql, param, ct));

    public async Task<int> ExecuteAsync(string sql, object? param = null, CancellationToken ct = default)
        => await db.GetDbConnection().ExecuteAsync(await CommandAsync(sql, param, ct));

    public async Task<T?> ExecuteScalarAsync<T>(string sql, object? param = null, CancellationToken ct = default)
        => await db.GetDbConnection().ExecuteScalarAsync<T>(await CommandAsync(sql, param, ct));
}
