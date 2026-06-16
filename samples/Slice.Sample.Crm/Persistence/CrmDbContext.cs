using Microsoft.EntityFrameworkCore;
using Slice.Core.Ambient;
using Slice.EntityFrameworkCore;
using Slice.Sample.Crm.Domain.Leads;

namespace Slice.Sample.Crm.Persistence;

public sealed class CrmDbContext(DbContextOptions<CrmDbContext> options, ICurrentTenant currentTenant, IDataFilter dataFilter)
    : SliceDbContext(options, currentTenant, dataFilter)
{
    public DbSet<Lead> Leads => Set<Lead>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Lead>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
            b.Property(x => x.Status).HasConversion<int>();
            b.Property(x => x.Source).HasConversion<int>();

            b.OwnsOne(x => x.Name, n =>
            {
                n.Property(p => p.FirstName).HasColumnName("FirstName").IsRequired();
                n.Property(p => p.LastName).HasColumnName("LastName").IsRequired();
            });
            b.Navigation(x => x.Name).IsRequired();

            b.OwnsOne(x => x.Contact, c =>
            {
                c.Property(p => p.Email).HasColumnName("Email");
                c.Property(p => p.Phone).HasColumnName("Phone");
            });
            b.Navigation(x => x.Contact).IsRequired();
        });

        // base applies the soft-delete global query filter.
        base.OnModelCreating(modelBuilder);
    }
}
