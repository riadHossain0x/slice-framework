using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Slice.Core.Ambient;
using Slice.EntityFrameworkCore;

namespace Slice.Data.Tests;

/// <summary>Marker a host defines for itself — the framework knows nothing about it.</summary>
public interface IOwnedRow
{
    Guid OwnerId { get; }
}

public sealed class Memo : IOwnedRow
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// A host context adding a row-level filter of its own on top of the framework's soft-delete and
/// multi-tenant ones, via <see cref="SliceDbContext.BuildAdditionalFilter{TEntity}"/>. Reads the
/// current owner from a mutable field so the test can prove EF re-evaluates per query rather than
/// baking the value into the cached model — the same rule the built-in filters follow.
/// </summary>
public sealed class OwnedDbContext(DbContextOptions<OwnedDbContext> options, ICurrentTenant tenant, IDataFilter filter)
    : SliceDbContext(options, tenant, filter)
{
    public Guid? CurrentOwnerId { get; set; }

    public DbSet<Memo> Memos => Set<Memo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Memo>(b =>
        {
            b.ToTable("Memos");
            b.HasKey(x => x.Id);
            b.Property(x => x.Text).IsRequired();
        });
        base.OnModelCreating(modelBuilder);
    }

    protected override Expression<Func<TEntity, bool>>? BuildAdditionalFilter<TEntity>()
    {
        if (!typeof(IOwnedRow).IsAssignableFrom(typeof(TEntity)))
            return null;

        return e => CurrentOwnerId == null || EF.Property<Guid>(e, "OwnerId") == CurrentOwnerId;
    }
}

public sealed class Tag
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<Post> Posts { get; } = [];
}

public sealed class Post
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public List<Tag> Tags { get; } = [];
}

/// <summary>
/// The worst case for the model-walk loop: an implicit many-to-many — which EF models as a shared-type
/// entity over <c>Dictionary&lt;string, object&gt;</c> — combined with a
/// <see cref="SliceDbContext.BuildAdditionalFilter{TEntity}"/> that answers for <em>every</em> type.
/// A host keyed on a marker all its entities satisfy would look exactly like this.
/// </summary>
public sealed class SharedTypeDbContext(DbContextOptions<SharedTypeDbContext> options, ICurrentTenant tenant, IDataFilter filter)
    : SliceDbContext(options, tenant, filter)
{
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<Tag> Tags => Set<Tag>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Post>().HasMany(p => p.Tags).WithMany(t => t.Posts);
        base.OnModelCreating(modelBuilder);
    }

    protected override Expression<Func<TEntity, bool>>? BuildAdditionalFilter<TEntity>() => _ => true;
}

public sealed class SharedTypeEntityFilterTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"slice-sharedtype-{Guid.NewGuid():N}");

    private SharedTypeDbContext NewContext() => new(
        new DbContextOptionsBuilder<SharedTypeDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_dir, "shared.db")}").Options,
        new NullCurrentTenant(),
        new DataFilter());

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_dir);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_shared_type_entity_type_is_skipped_by_the_filter_walk()
    {
        // Before shared-CLR-type entity types were skipped, offering the join to the filter builder made
        // modelBuilder.Entity<Dictionary<string, object>>() fail the entire model build: "The entity
        // type 'Dictionary<string, object>' cannot be added to the model because its CLR type has been
        // configured as a shared type." A hard startup failure, not a subtly wrong query.
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();

        var joins = db.Model.GetEntityTypes()
            .Where(e => e.ClrType == typeof(Dictionary<string, object>))
            .ToList();

        Assert.Single(joins);
        Assert.True(joins[0].HasSharedClrType);
    }

    [Fact]
    public async Task The_many_to_many_still_round_trips()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();

        var post = new Post { Id = Guid.NewGuid(), Title = "p" };
        post.Tags.Add(new Tag { Id = Guid.NewGuid(), Name = "t" });
        db.Posts.Add(post);
        await db.SaveChangesAsync();

        await using var read = NewContext();
        var loaded = Assert.Single(await read.Posts.Include(p => p.Tags).ToListAsync());
        Assert.Equal("t", Assert.Single(loaded.Tags).Name);
    }
}

public sealed class AdditionalQueryFilterTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"slice-ownfilter-{Guid.NewGuid():N}");
    private readonly Guid _alice = Guid.NewGuid();
    private readonly Guid _bob = Guid.NewGuid();

    private OwnedDbContext NewContext() => new(
        new DbContextOptionsBuilder<OwnedDbContext>().UseSqlite($"Data Source={Path.Combine(_dir, "own.db")}").Options,
        new NullCurrentTenant(),
        new DataFilter());

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_dir);
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();

        db.Memos.Add(new Memo { Id = Guid.NewGuid(), OwnerId = _alice, Text = "alice's" });
        db.Memos.Add(new Memo { Id = Guid.NewGuid(), OwnerId = _bob, Text = "bob's" });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_host_filter_applies_to_entities_the_framework_knows_nothing_about()
    {
        // Memo implements neither ISoftDelete nor IMultiTenant, so before the seam existed it was
        // never even offered to the filter builder.
        await using var db = NewContext();
        db.CurrentOwnerId = _alice;

        var visible = await db.Memos.ToListAsync();

        Assert.Equal("alice's", Assert.Single(visible).Text);
    }

    [Fact]
    public async Task The_filter_is_re_evaluated_per_query_not_baked_into_the_model()
    {
        await using var db = NewContext();

        db.CurrentOwnerId = _alice;
        Assert.Equal("alice's", (await db.Memos.ToListAsync()).Single().Text);

        db.CurrentOwnerId = _bob;
        Assert.Equal("bob's", (await db.Memos.ToListAsync()).Single().Text);
    }

    [Fact]
    public async Task Returning_null_leaves_the_entity_unfiltered()
    {
        await using var db = NewContext();
        db.CurrentOwnerId = null;

        Assert.Equal(2, (await db.Memos.ToListAsync()).Count);
    }

    [Fact]
    public async Task IgnoreQueryFilters_still_escapes_it()
    {
        await using var db = NewContext();
        db.CurrentOwnerId = _alice;

        Assert.Equal(2, (await db.Memos.IgnoreQueryFilters().ToListAsync()).Count);
    }
}
