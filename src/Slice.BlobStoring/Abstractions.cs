using Slice.Core.DependencyInjection;

namespace Slice.BlobStoring;

/// <summary>Overrides the container name derived from the container marker type.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BlobContainerNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>A typed blob container (one marker class per logical container, ABP-style).</summary>
public interface IBlobContainer
{
    Task SaveAsync(string name, Stream stream, bool overrideExisting = false, CancellationToken ct = default);
    Task<Stream?> GetOrNullAsync(string name, CancellationToken ct = default);
    Task<bool> ExistsAsync(string name, CancellationToken ct = default);
    Task<bool> DeleteAsync(string name, CancellationToken ct = default);
}

public interface IBlobContainer<TContainer> : IBlobContainer;

/// <summary>The storage backend (FileSystem / Azure / S3). Keyed by (container, blob).</summary>
public interface IBlobProvider
{
    Task SaveAsync(string container, string blob, Stream stream, bool overrideExisting, CancellationToken ct);
    Task<Stream?> GetOrNullAsync(string container, string blob, CancellationToken ct);
    Task<bool> ExistsAsync(string container, string blob, CancellationToken ct);
    Task<bool> DeleteAsync(string container, string blob, CancellationToken ct);
}

/// <summary>Resolves a container marker type to its (lower-case) container name.</summary>
public interface IBlobContainerNameResolver
{
    string Resolve(Type containerType);
}

public sealed class BlobContainerNameResolver : IBlobContainerNameResolver, ISingletonDependency
{
    public string Resolve(Type containerType)
    {
        var attr = (BlobContainerNameAttribute?)Attribute.GetCustomAttribute(containerType, typeof(BlobContainerNameAttribute));
        if (attr is not null) return attr.Name;
        var name = containerType.Name;
        if (name.EndsWith("Container", StringComparison.Ordinal))
            name = name[..^"Container".Length];
        return name.ToLowerInvariant();
    }
}

public sealed class BlobContainer<TContainer>(IBlobProvider provider, IBlobContainerNameResolver resolver)
    : IBlobContainer<TContainer>
{
    private readonly string _container = resolver.Resolve(typeof(TContainer));

    public Task SaveAsync(string name, Stream stream, bool overrideExisting = false, CancellationToken ct = default)
        => provider.SaveAsync(_container, name, stream, overrideExisting, ct);
    public Task<Stream?> GetOrNullAsync(string name, CancellationToken ct = default)
        => provider.GetOrNullAsync(_container, name, ct);
    public Task<bool> ExistsAsync(string name, CancellationToken ct = default)
        => provider.ExistsAsync(_container, name, ct);
    public Task<bool> DeleteAsync(string name, CancellationToken ct = default)
        => provider.DeleteAsync(_container, name, ct);
}
