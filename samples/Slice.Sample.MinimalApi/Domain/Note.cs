using Slice.Domain.Entities;
using Slice.Domain.Repositories;

namespace Slice.Sample.MinimalApi.Domain;

/// <summary>A minimal aggregate: a note with a title and body. State changes go through business methods.</summary>
public sealed class Note : FullAuditedAggregateRoot<Guid>
{
    private Note() { } // ORM

    public Note(Guid id, string title, string body) : base(id)
    {
        Title = title;
        Body = body;
    }

    public string Title { get; private set; } = "";
    public string Body { get; private set; } = "";

    public void Update(string title, string body)
    {
        Title = title;
        Body = body;
    }
}

public interface INoteRepository : IRepository<Note, Guid>;
