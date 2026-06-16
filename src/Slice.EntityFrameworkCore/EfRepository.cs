using Microsoft.EntityFrameworkCore;
using Slice.Domain.Entities;
using Slice.Domain.Exceptions;
using Slice.Domain.Repositories;
using Slice.Domain.Specifications;

namespace Slice.EntityFrameworkCore;

/// <summary>
/// EF Core repository over an aggregate. Inserts/updates track changes; the unit-of-work
/// behavior flushes them (pass <c>autoSave: true</c> to save immediately outside a UoW).
/// Override <see cref="WithDetails"/> in a custom repository to eager-load child collections.
/// </summary>
public class EfRepository<TContext, TEntity, TKey>(TContext db) : IRepository<TEntity, TKey>
    where TContext : DbContext
    where TEntity : class, IAggregateRoot<TKey>
    where TKey : notnull
{
    protected TContext Db { get; } = db;
    protected DbSet<TEntity> Set => Db.Set<TEntity>();

    protected virtual IQueryable<TEntity> WithDetails(IQueryable<TEntity> query) => query;

    public virtual async Task<TEntity?> FindAsync(TKey id, CancellationToken ct = default)
        => await Set.FindAsync([id], ct);

    public virtual async Task<TEntity> GetAsync(TKey id, CancellationToken ct = default)
        => await FindAsync(id, ct) ?? throw EntityNotFoundException.For<TEntity>(id);

    public virtual async Task<List<TEntity>> GetListAsync(
        ISpecification<TEntity>? specification = null, CancellationToken ct = default)
    {
        IQueryable<TEntity> query = Set;
        if (specification is not null)
            query = query.Where(specification.ToExpression());
        return await query.ToListAsync(ct);
    }

    public virtual async Task<long> GetCountAsync(
        ISpecification<TEntity>? specification = null, CancellationToken ct = default)
    {
        IQueryable<TEntity> query = Set;
        if (specification is not null)
            query = query.Where(specification.ToExpression());
        return await query.LongCountAsync(ct);
    }

    public virtual Task<IQueryable<TEntity>> GetQueryableAsync() => Task.FromResult<IQueryable<TEntity>>(Set);

    public virtual async Task<TEntity> InsertAsync(TEntity entity, bool autoSave = false, CancellationToken ct = default)
    {
        await Set.AddAsync(entity, ct);
        if (autoSave) await Db.SaveChangesAsync(ct);
        return entity;
    }

    public virtual async Task<TEntity> UpdateAsync(TEntity entity, bool autoSave = false, CancellationToken ct = default)
    {
        Set.Update(entity);
        if (autoSave) await Db.SaveChangesAsync(ct);
        return entity;
    }

    public virtual async Task DeleteAsync(TEntity entity, bool autoSave = false, CancellationToken ct = default)
    {
        Set.Remove(entity); // soft-delete-aware via the auditing interceptor
        if (autoSave) await Db.SaveChangesAsync(ct);
    }

    public virtual async Task DeleteAsync(TKey id, bool autoSave = false, CancellationToken ct = default)
    {
        var entity = await FindAsync(id, ct);
        if (entity is not null)
            await DeleteAsync(entity, autoSave, ct);
    }
}
