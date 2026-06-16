using Microsoft.Extensions.DependencyInjection;
using Slice.Application;
using Slice.Modularity;

namespace Slice.Caching;

/// <summary>
/// Caching module: registers <see cref="ISliceCache"/> and a default in-memory
/// <c>IDistributedCache</c>. Swap in Redis via <c>Slice.Caching.Redis</c>'s AddSliceRedisCache.
/// </summary>
[DependsOn(typeof(SliceApplicationModule))]
public sealed class SliceCachingModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddDistributedMemoryCache(); // default; replaced by the Redis adapter
        context.Services.AddSliceConventions(typeof(SliceCachingModule).Assembly);
    }
}
