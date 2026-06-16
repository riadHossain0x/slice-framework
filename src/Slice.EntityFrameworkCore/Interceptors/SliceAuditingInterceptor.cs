using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Slice.Core.Ambient;
using Slice.Core.DependencyInjection;
using Slice.Domain.Auditing;

namespace Slice.EntityFrameworkCore.Interceptors;

/// <summary>
/// Stamps creation/modification audit fields, regenerates concurrency stamps, and converts
/// hard deletes of <see cref="ISoftDelete"/> entities into soft deletes.
/// </summary>
public sealed class SliceAuditingInterceptor(IClock clock, ICurrentUser currentUser)
    : SaveChangesInterceptor, ISingletonDependency
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
            return;

        var now = clock.Now;
        var userId = currentUser.Id;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity is IHasCreationTime created) created.CreationTime = now;
                    if (entry.Entity is IMayHaveCreator hasCreator) hasCreator.CreatorId = userId;
                    break;

                case EntityState.Modified:
                    Touch(entry.Entity, now, userId);
                    break;

                case EntityState.Deleted when entry.Entity is ISoftDelete softDelete:
                    entry.State = EntityState.Modified;
                    softDelete.IsDeleted = true;
                    if (entry.Entity is IHasDeletionTime deletion)
                    {
                        deletion.DeletionTime = now;
                        deletion.DeleterId = userId;
                    }
                    Touch(entry.Entity, now, userId);
                    break;
            }
        }
    }

    private static void Touch(object entity, DateTime now, Guid? userId)
    {
        if (entity is IModificationAuditedObject modified)
        {
            modified.LastModificationTime = now;
            modified.LastModifierId = userId;
        }
        else if (entity is IHasModificationTime hasModification)
        {
            hasModification.LastModificationTime = now;
        }

        if (entity is IHasConcurrencyStamp stamped)
            stamped.ConcurrencyStamp = Guid.NewGuid().ToString("N");
    }
}
