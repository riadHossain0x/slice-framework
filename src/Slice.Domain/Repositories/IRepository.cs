using Slice.Domain.Entities;
using Slice.Domain.Specifications;

namespace Slice.Domain.Repositories;

/// <summary>Read-only repository over an entity.</summary>
public interface IReadRepository<TEntity, in TKey> where TEntity : class, IEntity<TKey>
{
    Task<TEntity?> FindAsync(TKey id, CancellationToken ct = default);
    Task<TEntity> GetAsync(TKey id, CancellationToken ct = default);
    Task<List<TEntity>> GetListAsync(ISpecification<TEntity>? specification = null, CancellationToken ct = default);
    Task<long> GetCountAsync(ISpecification<TEntity>? specification = null, CancellationToken ct = default);
    Task<IQueryable<TEntity>> GetQueryableAsync();
}

/// <summary>Read/write repository over an aggregate root.</summary>
public interface IRepository<TEntity, TKey> : IReadRepository<TEntity, TKey>
    where TEntity : class, IAggregateRoot<TKey>
{
    Task<TEntity> InsertAsync(TEntity entity, bool autoSave = false, CancellationToken ct = default);
    Task<TEntity> UpdateAsync(TEntity entity, bool autoSave = false, CancellationToken ct = default);
    Task DeleteAsync(TEntity entity, bool autoSave = false, CancellationToken ct = default);
    Task DeleteAsync(TKey id, bool autoSave = false, CancellationToken ct = default);
}
