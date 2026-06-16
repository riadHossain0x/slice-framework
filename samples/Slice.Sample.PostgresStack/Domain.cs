using Slice.Domain.Entities;
using Slice.Domain.Events;
using Slice.Domain.Repositories;
using Slice.EventBus;

namespace Slice.Sample.PostgresStack;

[DistributedEventName("sample.note-created")]
public sealed record NoteCreatedEto(Guid Id, string Title) : IDistributedEvent;

public sealed class Note : AggregateRoot<Guid>
{
    private Note() { }

    public Note(Guid id, string title, string body) : base(id)
    {
        Title = title;
        Body = body;
        AddDistributedEvent(new NoteCreatedEto(id, title));   // → transactional outbox → Postgres event bus
    }

    public string Title { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
}

public interface INoteRepository : IRepository<Note, Guid>;
