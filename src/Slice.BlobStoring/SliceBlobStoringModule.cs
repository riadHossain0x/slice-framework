using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Slice.Application;
using Slice.Modularity;

namespace Slice.BlobStoring;

/// <summary>
/// Blob-storing module: registers typed <see cref="IBlobContainer{TContainer}"/> + name resolver
/// and a default <see cref="FileSystemBlobProvider"/>. Swap the backend with the Azure/AWS adapters.
/// </summary>
[DependsOn(typeof(SliceApplicationModule))]
public sealed class SliceBlobStoringModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var options = new FileSystemBlobOptions();
        var basePath = context.Configuration["BlobStoring:FileSystem:BasePath"];
        if (!string.IsNullOrWhiteSpace(basePath))
            options.BasePath = basePath;
        context.Services.AddSingleton(options);

        context.Services.AddSliceConventions(typeof(SliceBlobStoringModule).Assembly); // resolver + FileSystem provider
        context.Services.AddTransient(typeof(IBlobContainer<>), typeof(BlobContainer<>));
    }
}
