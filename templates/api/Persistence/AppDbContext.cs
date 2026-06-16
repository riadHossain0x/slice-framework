using Microsoft.EntityFrameworkCore;
using Slice.Core.Ambient;
using Slice.EntityFrameworkCore;
using SliceApp.Domain;

namespace SliceApp.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenant currentTenant, IDataFilter dataFilter)
    : SliceDbContext(options, currentTenant, dataFilter)
{
    public DbSet<Note> Notes => Set<Note>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Note>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
            b.Property(x => x.Title).IsRequired().HasMaxLength(256);
        });

        // base applies the soft-delete + multi-tenant global query filters.
        base.OnModelCreating(modelBuilder);
    }
}

public sealed class NoteRepository(AppDbContext db) : EfRepository<AppDbContext, Note, Guid>(db), INoteRepository;
