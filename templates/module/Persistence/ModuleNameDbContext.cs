using Microsoft.EntityFrameworkCore;
using Slice.Core.Ambient;
using Slice.EntityFrameworkCore;
using ModuleName.Domain;

namespace ModuleName.Persistence;

public sealed class ModuleNameDbContext(DbContextOptions<ModuleNameDbContext> options, ICurrentTenant currentTenant, IDataFilter dataFilter)
    : SliceDbContext(options, currentTenant, dataFilter)
{
    public DbSet<Item> Items => Set<Item>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Item>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
            b.Property(x => x.Name).IsRequired().HasMaxLength(256);
        });

        // base applies the soft-delete + multi-tenant global query filters.
        base.OnModelCreating(modelBuilder);
    }
}

public sealed class ItemRepository(ModuleNameDbContext db) : EfRepository<ModuleNameDbContext, Item, Guid>(db), IItemRepository;
