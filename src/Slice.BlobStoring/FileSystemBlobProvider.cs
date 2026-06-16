using Slice.Core.DependencyInjection;

namespace Slice.BlobStoring;

public sealed class FileSystemBlobOptions
{
    public string BasePath { get; set; } = Path.Combine(AppContext.BaseDirectory, "blobs");
}

/// <summary>Default provider: stores blobs under <c>{BasePath}/{container}/{blob}</c> on disk.</summary>
public sealed class FileSystemBlobProvider(FileSystemBlobOptions options) : IBlobProvider, ISingletonDependency
{
    public async Task SaveAsync(string container, string blob, Stream stream, bool overrideExisting, CancellationToken ct)
    {
        var path = PathFor(container, blob);
        if (!overrideExisting && File.Exists(path))
            throw new InvalidOperationException($"Blob '{blob}' already exists in '{container}'.");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = File.Create(path);
        await stream.CopyToAsync(file, ct);
    }

    public Task<Stream?> GetOrNullAsync(string container, string blob, CancellationToken ct)
    {
        var path = PathFor(container, blob);
        Stream? result = File.Exists(path) ? File.OpenRead(path) : null;
        return Task.FromResult(result);
    }

    public Task<bool> ExistsAsync(string container, string blob, CancellationToken ct)
        => Task.FromResult(File.Exists(PathFor(container, blob)));

    public Task<bool> DeleteAsync(string container, string blob, CancellationToken ct)
    {
        var path = PathFor(container, blob);
        if (!File.Exists(path)) return Task.FromResult(false);
        File.Delete(path);
        return Task.FromResult(true);
    }

    private string PathFor(string container, string blob)
    {
        // Normalize blob separators; prevent path traversal out of the container root.
        var safeBlob = blob.Replace('\\', '/').TrimStart('/');
        var root = Path.GetFullPath(Path.Combine(options.BasePath, container));
        var full = Path.GetFullPath(Path.Combine(root, safeBlob));
        if (!full.StartsWith(root, StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid blob name.");
        return full;
    }
}
