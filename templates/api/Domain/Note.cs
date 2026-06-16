using Slice.Domain.Entities;
using Slice.Domain.MultiTenancy;
using Slice.Domain.Repositories;

namespace SliceApp.Domain;

/// <summary>
/// A minimal demo aggregate. Replace it with your own bounded-context aggregates.
/// State changes go through business methods — never public setters.
/// </summary>
public sealed class Note : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    private Note() { } // EF

    public Note(Guid id, Guid? tenantId, string title, string body) : base(id)
    {
        TenantId = tenantId;
        Rename(title);
        Edit(body);
    }

    public Guid? TenantId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;

    public void Rename(string title) => Title = (title ?? string.Empty).Trim();
    public void Edit(string body) => Body = body ?? string.Empty;
}

public interface INoteRepository : IRepository<Note, Guid>;
