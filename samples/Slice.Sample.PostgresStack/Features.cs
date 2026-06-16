using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Slice.Application;
using Slice.AspNetCore.Mvc;
using Slice.BackgroundJobs;
using Slice.Caching;
using Slice.Core.Ambient;
using Slice.Core.Results;
using Slice.EventBus;
using Slice.Vector;

namespace Slice.Sample.PostgresStack;

public static class NotesVector
{
    public const string Collection = "notes";
}

// ── Create a note: persists (EF→Postgres), raises an integration event (→ outbox → PG event bus),
//     indexes it for vector search, and enqueues a durable background job. One request, whole stack. ──
public sealed record CreateNoteCommand(string Title, string Body) : ICommand<Result<Guid>>;

public sealed class CreateNoteValidator : AbstractValidator<CreateNoteCommand>
{
    public CreateNoteValidator() => RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
}

public sealed class CreateNoteHandler(
    INoteRepository repository, IGuidGenerator guids,
    IVectorStore vectors, IEmbeddingGenerator embedder, IBackgroundJobManager jobs)
    : ICommandHandler<CreateNoteCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(CreateNoteCommand command, CancellationToken ct)
    {
        var note = new Note(guids.Create(), command.Title, command.Body);
        await repository.InsertAsync(note, autoSave: false, ct);

        var embedding = await embedder.GenerateAsync($"{command.Title}\n{command.Body}", ct);
        var collection = vectors.GetCollection(NotesVector.Collection, embedder.Dimensions);
        await collection.UpsertAsync(new VectorRecord(note.Id.ToString(), embedding, command.Title), ct);

        await jobs.EnqueueAsync(new IndexNoteArgs(note.Id, command.Title));
        return Result<Guid>.Success(note.Id);
    }
}

[Route("api/notes")]
public sealed class CreateNoteController : SliceController
{
    [HttpPost]
    public Task<IActionResult> Create([FromBody] CreateNoteCommand command, CancellationToken ct)
        => SendAsync(command, ct);
}

// ── List notes (+ a cached count to exercise the Postgres cache) ──
public sealed record NoteDto(Guid Id, string Title, string Body);

public sealed record ListNotesQuery : IQuery<Result<IReadOnlyList<NoteDto>>>;

public sealed class ListNotesHandler(INoteRepository repository, ISliceCache cache)
    : IQueryHandler<ListNotesQuery, Result<IReadOnlyList<NoteDto>>>
{
    public async Task<Result<IReadOnlyList<NoteDto>>> HandleAsync(ListNotesQuery query, CancellationToken ct)
    {
        var notes = await repository.GetListAsync(specification: null, ct);
        // Cache the count for 30s in Postgres (slice_cache) to show the cache adapter working.
        await cache.SetAsync("notes:count", notes.Count, TimeSpan.FromSeconds(30), ct);
        IReadOnlyList<NoteDto> dtos = notes.Select(n => new NoteDto(n.Id, n.Title, n.Body)).ToList();
        return Result<IReadOnlyList<NoteDto>>.Success(dtos);
    }
}

[Route("api/notes")]
public sealed class ListNotesController : SliceController
{
    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct) => SendAsync(new ListNotesQuery(), ct);
}

// ── Semantic search over notes via pgvector ──
public sealed record SearchNotesQuery(string Query, int TopK = 5) : IQuery<Result<IReadOnlyList<SearchHit>>>;
public sealed record SearchHit(string Id, string Title, double Score);

public sealed class SearchNotesHandler(IVectorStore vectors, IEmbeddingGenerator embedder)
    : IQueryHandler<SearchNotesQuery, Result<IReadOnlyList<SearchHit>>>
{
    public async Task<Result<IReadOnlyList<SearchHit>>> HandleAsync(SearchNotesQuery query, CancellationToken ct)
    {
        var embedding = await embedder.GenerateAsync(query.Query, ct);
        var collection = vectors.GetCollection(NotesVector.Collection, embedder.Dimensions);
        var results = await collection.SearchAsync(embedding, query.TopK, ct);
        IReadOnlyList<SearchHit> hits = results
            .Select(r => new SearchHit(r.Record.Id, r.Record.Content ?? "", r.Score)).ToList();
        return Result<IReadOnlyList<SearchHit>>.Success(hits);
    }
}

[Route("api/notes/search")]
public sealed class SearchNotesController : SliceController
{
    [HttpPost]
    public Task<IActionResult> Search([FromBody] SearchNotesQuery query, CancellationToken ct)
        => SendAsync(query, ct);
}

// ── An integration-event handler: fires when the outbox delivers NoteCreatedEto over the PG event bus ──
public sealed class NoteProjector(ILogger<NoteProjector> logger) : IDistributedEventHandler<NoteCreatedEto>
{
    public Task HandleAsync(NoteCreatedEto @event, CancellationToken ct)
    {
        logger.LogInformation("PROJECTED note {Id}: {Title} (delivered via Postgres event bus)", @event.Id, @event.Title);
        return Task.CompletedTask;
    }
}

// ── A durable background job (runs from slice_jobs) ──
public sealed record IndexNoteArgs(Guid Id, string Title);

public sealed class IndexNoteJob(ILogger<IndexNoteJob> logger) : IBackgroundJob<IndexNoteArgs>
{
    public Task ExecuteAsync(IndexNoteArgs args, CancellationToken ct)
    {
        logger.LogInformation("JOB indexed note {Id}: {Title} (ran from Postgres job queue)", args.Id, args.Title);
        return Task.CompletedTask;
    }
}
