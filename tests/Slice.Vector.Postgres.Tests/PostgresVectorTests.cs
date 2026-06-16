using Npgsql;
using Pgvector;
using Slice.Core.Ambient;
using Slice.Vector;
using Slice.Vector.Postgres;
using Testcontainers.PostgreSql;

namespace Slice.Vector.Postgres.Tests;

/// <summary>
/// Exercises the pgvector-backed store against a real Postgres+pgvector container: create a
/// collection, upsert embedded documents, and confirm nearest-neighbour search returns the match.
/// Uses the deterministic offline embedder so the test is reproducible.
/// </summary>
public sealed class PostgresVectorTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg =
        new PostgreSqlBuilder().WithImage("pgvector/pgvector:pg16").Build();

    private NpgsqlDataSource _dataSource = null!;

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        var builder = new NpgsqlDataSourceBuilder(_pg.GetConnectionString());
        builder.UseVector();
        _dataSource = builder.Build();
    }

    public async Task DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _pg.DisposeAsync();
    }

    [Fact]
    public async Task Upsert_then_search_returns_the_nearest_document()
    {
        var embedder = new HashingEmbeddingGenerator();
        var store = new PostgresVectorStore(_dataSource, new NullCurrentTenant());
        var collection = store.GetCollection("docs", embedder.Dimensions, VectorDistance.Cosine);
        await collection.EnsureCreatedAsync();

        await collection.UpsertBatchAsync(
        [
            new VectorRecord("cat", await embedder.GenerateAsync("the cat sat on the warm mat"), "cat"),
            new VectorRecord("physics", await embedder.GenerateAsync("quantum field theory and relativity"), "physics"),
            new VectorRecord("dog", await embedder.GenerateAsync("a loyal dog runs in the park"), "dog"),
        ]);

        var query = await embedder.GenerateAsync("the cat sat on the warm mat");
        var results = await collection.SearchAsync(query, topK: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("cat", results[0].Record.Id);          // exact match is nearest
        Assert.Equal("cat", results[0].Record.Content);
        Assert.True(results[0].Score <= results[1].Score);  // ordered by distance ascending

        // Update + delete round trip.
        await collection.DeleteAsync("cat");
        var afterDelete = await collection.SearchAsync(query, topK: 1);
        Assert.NotEqual("cat", afterDelete[0].Record.Id);
    }
}
