namespace Slice.Application.UnitOfWork;

/// <summary>
/// A persistence unit of work. Implemented by each DbContext; the
/// <c>UnitOfWorkBehavior</c> flushes all registered units after a command succeeds.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
