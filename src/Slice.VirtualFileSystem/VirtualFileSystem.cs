using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Slice.Core.DependencyInjection;
using Slice.Modularity;

namespace Slice.VirtualFileSystem;

/// <summary>
/// A composite, read-only file system over embedded resources (from registered assemblies) and an
/// optional physical root. Used for localization JSON, email templates, seed data, etc.
/// </summary>
public interface IVirtualFileProvider
{
    IFileInfo GetFileInfo(string path);
    Task<string?> ReadAsStringAsync(string path);
}

public sealed class VirtualFileSystemOptions
{
    internal List<IFileProvider> Providers { get; } = [];

    /// <summary>Maps embedded resources of <typeparamref name="TMarker"/>'s assembly under <paramref name="baseNamespace"/>.</summary>
    public VirtualFileSystemOptions AddEmbedded<TMarker>(string? baseNamespace = null)
    {
        var assembly = typeof(TMarker).Assembly;
        Providers.Add(new EmbeddedFileProvider(assembly, baseNamespace ?? assembly.GetName().Name));
        return this;
    }

    public VirtualFileSystemOptions AddPhysical(string root)
    {
        Providers.Add(new PhysicalFileProvider(root));
        return this;
    }
}

public sealed class VirtualFileProvider(VirtualFileSystemOptions options) : IVirtualFileProvider, ISingletonDependency
{
    private readonly IFileProvider _composite = new CompositeFileProvider(options.Providers);

    public IFileInfo GetFileInfo(string path) => _composite.GetFileInfo(path);

    public async Task<string?> ReadAsStringAsync(string path)
    {
        var file = _composite.GetFileInfo(path);
        if (!file.Exists) return null;
        await using var stream = file.CreateReadStream();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}

/// <summary>Virtual file-system module. Configure providers with <see cref="ConfigureVirtualFileSystem"/>.</summary>
public sealed class SliceVirtualFileSystemModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
        => context.Services.AddSliceConventions(typeof(SliceVirtualFileSystemModule).Assembly);
}

public static class VirtualFileSystemRegistration
{
    public static IServiceCollection ConfigureVirtualFileSystem(this IServiceCollection services, Action<VirtualFileSystemOptions> configure)
    {
        var options = new VirtualFileSystemOptions();
        configure(options);
        services.AddSingleton(options);
        return services;
    }
}
