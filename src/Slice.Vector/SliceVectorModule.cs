using Microsoft.Extensions.DependencyInjection;
using Slice.Application;
using Slice.Modularity;

namespace Slice.Vector;

/// <summary>
/// Vector-search module: registers the default offline <see cref="IEmbeddingGenerator"/>
/// (<see cref="HashingEmbeddingGenerator"/>). An <see cref="IVectorStore"/> is supplied by an adapter
/// (e.g. <c>Slice.Vector.Postgres</c>); a real embedder (e.g. <c>Slice.Embeddings.OpenAI</c>) replaces
/// the default.
/// </summary>
[DependsOn(typeof(SliceApplicationModule))]
public sealed class SliceVectorModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
        => context.Services.AddSliceConventions(typeof(SliceVectorModule).Assembly);
}
