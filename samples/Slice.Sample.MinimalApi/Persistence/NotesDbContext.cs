using Microsoft.EntityFrameworkCore;
using Slice.Core.Ambient;
using Slice.EntityFrameworkCore;
using Slice.Sample.MinimalApi.Domain;

namespace Slice.Sample.MinimalApi.Persistence;

public sealed class NotesDbContext(DbContextOptions<NotesDbContext> options, ICurrentTenant currentTenant, IDataFilter dataFilter)
    : SliceDbContext(options, currentTenant, dataFilter)
{
    public DbSet<Note> Notes => Set<Note>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Note>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
            b.Property(x => x.Title).IsRequired().HasMaxLength(200);
            b.Property(x => x.Body).IsRequired();
        });

        base.OnModelCreating(modelBuilder);
    }
}

public sealed class EfNoteRepository(NotesDbContext db) : EfRepository<NotesDbContext, Note, Guid>(db), INoteRepository;
