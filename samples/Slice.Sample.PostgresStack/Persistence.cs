using Microsoft.EntityFrameworkCore;
using Slice.Core.Ambient;
using Slice.EntityFrameworkCore;

namespace Slice.Sample.PostgresStack;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenant tenant, IDataFilter filter)
    : SliceDbContext(options, tenant, filter)
{
    public DbSet<Note> Notes => Set<Note>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Note>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
            e.Property(x => x.Title).IsRequired();
        });
        base.OnModelCreating(b);
    }
}

public sealed class EfNoteRepository(AppDbContext db) : EfRepository<AppDbContext, Note, Guid>(db), INoteRepository;
