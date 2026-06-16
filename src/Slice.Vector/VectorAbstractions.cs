namespace Slice.Vector;

/// <summary>The distance metric a collection uses for nearest-neighbour search.</summary>
public enum VectorDistance
{
    /// <summary>Cosine distance (1 − cosine similarity). Good default for normalised embeddings.</summary>
    Cosine,
    /// <summary>Euclidean (L2) distance.</summary>
    L2,
    /// <summary>Negative inner product.</summary>
    InnerProduct
}

/// <summary>A stored vector: an id, the embedding, and optional content/metadata/tenant.</summary>
public sealed record VectorRecord(
    string Id,
    float[] Embedding,
    string? Content = null,
    IReadOnlyDictionary<string, object?>? Metadata = null,
    Guid? TenantId = null);

/// <summary>A search hit: the stored record and its distance-derived score (lower = nearer).</summary>
public sealed record VectorSearchResult(VectorRecord Record, double Score);

/// <summary>
/// A named, fixed-dimension vector collection. Persists embeddings and answers top-K nearest-neighbour
/// queries. Tenant-aware: records carry an optional <c>TenantId</c> and searches filter to the current
/// tenant when one is set.
/// </summary>
public interface IVectorCollection
{
    string Name { get; }
    int Dimensions { get; }
    VectorDistance Distance { get; }

    Task EnsureCreatedAsync(CancellationToken ct = default);
    Task UpsertAsync(VectorRecord record, CancellationToken ct = default);
    Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(float[] query, int topK, CancellationToken ct = default);
}

/// <summary>Entry point: resolves (and lazily provisions) vector collections by name.</summary>
public interface IVectorStore
{
    IVectorCollection GetCollection(string name, int dimensions, VectorDistance distance = VectorDistance.Cosine);
}

/// <summary>
/// Turns text into an embedding vector. The vector store accepts raw <c>float[]</c>, so an embedder is
/// optional — but pairing one with a store gives you semantic search over text. The default is the
/// offline <see cref="HashingEmbeddingGenerator"/>; swap in a real model (e.g. OpenAI) for semantics.
/// </summary>
public interface IEmbeddingGenerator
{
    int Dimensions { get; }
    Task<float[]> GenerateAsync(string text, CancellationToken ct = default);
    Task<IReadOnlyList<float[]>> GenerateBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}
