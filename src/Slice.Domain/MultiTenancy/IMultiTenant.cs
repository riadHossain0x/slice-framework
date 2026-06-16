namespace Slice.Domain.MultiTenancy;

/// <summary>Marks an entity as tenant-scoped. The framework stamps and filters by <see cref="TenantId"/>.</summary>
public interface IMultiTenant
{
    Guid? TenantId { get; }
}
