using LinqToDB;
using LinqToDB.Data;
using LinqToDB.DataProvider;
using Slice.EntityFrameworkCore;

namespace Slice.LinqToDB;

/// <summary>Holds the LinqToDB data provider chosen for <typeparamref name="TContext"/>.</summary>
public sealed class SliceLinqToDbOptions<TContext>(IDataProvider dataProvider) where TContext : SliceDbContext
{
    public IDataProvider DataProvider { get; } = dataProvider;
}

/// <summary>
/// Creates a LinqToDB <see cref="DataConnection"/> bound to a <see cref="SliceDbContext"/>'s ADO.NET
/// connection and ambient transaction. LinqToDB queries therefore run inside the same unit of work
/// and transaction as EF Core changes — a genuinely different ORM over one shared connection.
/// </summary>
public interface ISliceDataConnectionFactory<TContext> where TContext : SliceDbContext
{
    Task<DataConnection> CreateAsync(CancellationToken ct = default);
}

public sealed class SliceDataConnectionFactory<TContext>(TContext db, SliceLinqToDbOptions<TContext> options)
    : ISliceDataConnectionFactory<TContext> where TContext : SliceDbContext
{
    public async Task<DataConnection> CreateAsync(CancellationToken ct = default)
    {
        await db.GetOpenConnectionAsync(ct);
        var provider = options.DataProvider;
        var transaction = db.GetCurrentTransaction();

        // Reuse the EF connection (and transaction, if one is active) without owning their lifetime.
        var dataOptions = transaction is not null
            ? new DataOptions().UseTransaction(provider, transaction)
            : new DataOptions().UseConnection(provider, db.GetDbConnection(), disposeConnection: false);

        return new DataConnection(dataOptions);
    }
}
