using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Slice.Application.UnitOfWork;
using Slice.Core.Ambient;
using Slice.Domain.Auditing;
using Slice.EntityFrameworkCore.ExtraProperties;
using Slice.Domain.MultiTenancy;
using Slice.EntityFrameworkCore.Outbox;

namespace Slice.EntityFrameworkCore;

/// <summary>
/// Base DbContext. Implements <see cref="IUnitOfWork"/> and applies the soft-delete + multi-tenant
/// global query filters to every applicable entity. Audit stamping and domain-event dispatch are
/// handled by registered interceptors.
/// </summary>
/// <remarks>
/// Filters reference the context <em>instance</em> members <see cref="CurrentTenantId"/> and the
/// data-filter toggles, so EF Core re-evaluates them per query (per the documented pattern) rather
/// than baking a constant into the cached model.
/// </remarks>
public abstract class SliceDbContext : DbContext, IUnitOfWork
{
    private static readonly MethodInfo ConfigureFiltersMethod =
        typeof(SliceDbContext).GetMethod(nameof(ConfigureGlobalFilters), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private readonly ICurrentTenant _currentTenant;
    private readonly IDataFilter _dataFilter;

    protected SliceDbContext(DbContextOptions options, ICurrentTenant currentTenant, IDataFilter dataFilter)
        : base(options)
    {
        _currentTenant = currentTenant;
        _dataFilter = dataFilter;
    }

    protected Guid? CurrentTenantId => _currentTenant.Id;
    protected bool IsSoftDeleteFilterEnabled => _dataFilter.IsEnabled<ISoftDelete>();
    protected bool IsMultiTenantFilterEnabled => _dataFilter.IsEnabled<IMultiTenant>();

    /// <summary>Transactional outbox — every Slice context carries one.</summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>
    /// The underlying ADO.NET connection for this context. Alternative ORMs (Dapper, LinqToDB) run
    /// on this same connection so they participate in the unit of work and any ambient transaction.
    /// </summary>
    public DbConnection GetDbConnection() => Database.GetDbConnection();

    /// <summary>The ambient EF transaction as an ADO.NET transaction, or null if none is active.</summary>
    public DbTransaction? GetCurrentTransaction() => Database.CurrentTransaction?.GetDbTransaction();

    /// <summary>Returns the connection, opening it first if it is not already open.</summary>
    public async Task<DbConnection> GetOpenConnectionAsync(CancellationToken ct = default)
    {
        var connection = GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await Database.OpenConnectionAsync(ct);
        return connection;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("SliceOutbox");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.ProcessedAt);
        });

        modelBuilder.Entity<InboxMessage>(b =>
        {
            b.ToTable("SliceInbox");
            b.HasKey(x => x.MessageId);
        });

        // Every non-owned entity is offered to the filter builder, not just ISoftDelete/IMultiTenant
        // ones — a derived context may add filters of its own via BuildAdditionalFilter, keyed on
        // whatever marker interface it likes, and the framework can't know what that is.
        // BuildFilter returns null when nothing applies, and no filter is registered in that case.
        //
        // Shared-CLR-type entity types are skipped. An implicit many-to-many join is modelled as a
        // shared-type entity over Dictionary<string, object>, so ConfigureGlobalFilters would close over
        // that dictionary type rather than the join entity — and if a derived context's
        // BuildAdditionalFilter returned a filter for it, modelBuilder.Entity<Dictionary<string,
        // object>>() fails the whole model build with "The entity type 'Dictionary<string, object>'
        // cannot be added to the model because its CLR type has been configured as a shared type."
        // Nothing is lost by not offering them: a shared-type entity has no CLR type of its own to
        // carry a marker interface, so no filter could meaningfully key on it.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.IsOwned() || entityType.HasSharedClrType)
                continue;

            ConfigureFiltersMethod.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
        }

        // Map the ExtraProperties JSON column on every IHasExtraProperties entity (e.g. all aggregates).
        modelBuilder.ConfigureExtraProperties(Database.ProviderName);
    }

    private void ConfigureGlobalFilters<TEntity>(ModelBuilder modelBuilder) where TEntity : class
    {
        var filter = BuildFilter<TEntity>();
        if (filter is not null)
            modelBuilder.Entity<TEntity>().HasQueryFilter(filter);
    }

    /// <summary>
    /// An extra global query filter for <typeparamref name="TEntity"/>, ANDed with the built-in
    /// soft-delete and multi-tenant ones. Return null (the default) for entities you don't filter.
    ///
    /// Called for every non-owned entity type in the model, so a derived context can key on any
    /// marker interface it defines — row ownership, branch, archive state — without the framework
    /// needing to know about it. Follow the same rule the built-ins do: reference context
    /// <em>instance</em> members rather than captured values, so EF re-evaluates the filter per query
    /// instead of baking a constant into the cached model. Pair it with an <see cref="IDataFilter"/>
    /// toggle if callers need to disable it.
    /// </summary>
    protected virtual Expression<Func<TEntity, bool>>? BuildAdditionalFilter<TEntity>() where TEntity : class => null;

    private Expression<Func<TEntity, bool>>? BuildFilter<TEntity>() where TEntity : class
    {
        Expression<Func<TEntity, bool>>? filter = null;

        if (typeof(ISoftDelete).IsAssignableFrom(typeof(TEntity)))
            filter = e => !IsSoftDeleteFilterEnabled || !EF.Property<bool>(e, "IsDeleted");

        if (typeof(IMultiTenant).IsAssignableFrom(typeof(TEntity)))
        {
            Expression<Func<TEntity, bool>> tenantFilter =
                e => !IsMultiTenantFilterEnabled || EF.Property<Guid?>(e, "TenantId") == CurrentTenantId;
            filter = filter is null ? tenantFilter : Combine(filter, tenantFilter);
        }

        if (BuildAdditionalFilter<TEntity>() is { } additional)
            filter = filter is null ? additional : Combine(filter, additional);

        return filter;
    }

    private static Expression<Func<TEntity, bool>> Combine<TEntity>(
        Expression<Func<TEntity, bool>> left, Expression<Func<TEntity, bool>> right)
    {
        var parameter = Expression.Parameter(typeof(TEntity), "e");
        var body = Expression.AndAlso(
            new ReplaceParameter(left.Parameters[0], parameter).Visit(left.Body)!,
            new ReplaceParameter(right.Parameters[0], parameter).Visit(right.Body)!);
        return Expression.Lambda<Func<TEntity, bool>>(body, parameter);
    }

    private sealed class ReplaceParameter(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
