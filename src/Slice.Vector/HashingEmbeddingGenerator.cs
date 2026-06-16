using Slice.Core.DependencyInjection;

namespace Slice.Vector;

/// <summary>
/// A deterministic, offline embedding generator (feature-hashing bag-of-words, L2-normalised). It is
/// <b>not semantic</b> — identical text yields identical vectors and shared words pull vectors together
/// — but it needs no model or network, which makes it a safe default for development and tests. Replace
/// it with a real generator (e.g. <c>Slice.Embeddings.OpenAI</c>) for production semantic search.
/// </summary>
public sealed class HashingEmbeddingGenerator : IEmbeddingGenerator, ISingletonDependency
{
    public int Dimensions => 256;

    public Task<float[]> GenerateAsync(string text, CancellationToken ct = default)
        => Task.FromResult(Embed(text));

    public Task<IReadOnlyList<float[]>> GenerateBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<float[]>>(texts.Select(Embed).ToList());

    private float[] Embed(string text)
    {
        var vector = new float[Dimensions];
        foreach (var token in Tokenize(text))
        {
            var slot = (int)(Fnv1a(token) % (uint)Dimensions);
            vector[slot] += 1f;
        }

        // L2-normalise so cosine distance behaves.
        var norm = (float)Math.Sqrt(vector.Sum(v => v * v));
        if (norm > 0)
            for (var i = 0; i < vector.Length; i++)
                vector[i] /= norm;

        return vector;
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var current = new System.Text.StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                current.Append(char.ToLowerInvariant(ch));
            }
            else if (current.Length > 0)
            {
                yield return current.ToString();
                current.Clear();
            }
        }
        if (current.Length > 0)
            yield return current.ToString();
    }

    private static uint Fnv1a(string token)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;
        foreach (var ch in token)
        {
            hash ^= ch;
            hash *= prime;
        }
        return hash;
    }
}
