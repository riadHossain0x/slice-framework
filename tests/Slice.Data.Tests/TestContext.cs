using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Slice.Core.Ambient;
using Slice.Domain.Entities;
using Slice.EntityFrameworkCore;

namespace Slice.Data.Tests;

public sealed class Widget
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>An aggregate — inherits <c>ExtraProperties</c> from <see cref="AggregateRoot{TKey}"/>.</summary>
public sealed class Gizmo : AggregateRoot<Guid>
{
    private Gizmo() { }
    public Gizmo(Guid id, string name) : base(id) => Name = name;
    public string Name { get; private set; } = string.Empty;
}

public sealed class TestDbContext(DbContextOptions<TestDbContext> options, ICurrentTenant currentTenant, IDataFilter dataFilter)
    : SliceDbContext(options, currentTenant, dataFilter)
{
    public DbSet<Widget> Widgets => Set<Widget>();
    public DbSet<Gizmo> Gizmos => Set<Gizmo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Widget>(b =>
        {
            b.ToTable("Widgets");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).IsRequired();
        });
        modelBuilder.Entity<Gizmo>(b =>
        {
            b.ToTable("Gizmos");
            b.HasKey(x => x.Id);
            b.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
            b.Property(x => x.Name).IsRequired();
        });
        base.OnModelCreating(modelBuilder);   // applies ConfigureExtraProperties → Gizmo gets the column
    }
}
