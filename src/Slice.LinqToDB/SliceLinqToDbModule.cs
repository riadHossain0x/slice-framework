using LinqToDB.DataProvider;
using Microsoft.Extensions.DependencyInjection;
using Slice.EntityFrameworkCore;
using Slice.Modularity;

namespace Slice.LinqToDB;

/// <summary>
/// LinqToDB integration: a second ORM that shares the EF connection + transaction (unit of work).
/// Call <see cref="SliceLinqToDbRegistration.AddSliceLinqToDb{TContext}"/> per DbContext.
/// </summary>
[DependsOn(typeof(SliceEntityFrameworkCoreModule))]
public sealed class SliceLinqToDbModule : SliceModule;

public static class SliceLinqToDbRegistration
{
    /// <summary>
    /// Registers a LinqToDB connection factory for <typeparamref name="TContext"/>. The host supplies
    /// the LinqToDB <see cref="IDataProvider"/> matching its database (e.g.
    /// <c>SQLiteTools.GetDataProvider()</c> or <c>PostgreSQLTools.GetDataProvider()</c>).
    /// </summary>
    public static IServiceCollection AddSliceLinqToDb<TContext>(
        this IServiceCollection services, IDataProvider dataProvider)
        where TContext : SliceDbContext
    {
        services.AddSingleton(new SliceLinqToDbOptions<TContext>(dataProvider));
        services.AddScoped<ISliceDataConnectionFactory<TContext>, SliceDataConnectionFactory<TContext>>();
        return services;
    }
}
