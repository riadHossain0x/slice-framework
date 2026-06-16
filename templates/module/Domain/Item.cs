using Slice.Domain.Entities;
using Slice.Domain.MultiTenancy;
using Slice.Domain.Repositories;

namespace ModuleName.Domain;

/// <summary>
/// A starter aggregate for this bounded context. Replace with your real aggregates.
/// State changes go through business methods — never public setters.
/// </summary>
public sealed class Item : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    private Item() { } // EF

    public Item(Guid id, Guid? tenantId, string name) : base(id)
    {
        TenantId = tenantId;
        Rename(name);
    }

    public Guid? TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;

    public void Rename(string name) => Name = (name ?? string.Empty).Trim();
}

public interface IItemRepository : IRepository<Item, Guid>;
