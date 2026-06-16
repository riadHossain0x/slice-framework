using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using Slice.BlobStoring;

namespace Slice.BlobStoring.Minio;

public sealed class MinioBlobOptions
{
    /// <summary>Host and port, e.g. <c>localhost:9000</c>.</summary>
    public required string Endpoint { get; init; }
    public required string AccessKey { get; init; }
    public required string SecretKey { get; init; }
    public required string Bucket { get; init; }
    public bool UseSsl { get; init; }
    public string? Region { get; init; }
    /// <summary>Create the bucket on first use if it does not exist.</summary>
    public bool CreateBucketIfNotExists { get; init; } = true;
}

/// <summary>
/// MinIO (S3-compatible) backend. Blobs are keyed <c>{container}/{blob}</c> within a single bucket,
/// mirroring the AWS provider so containers map cleanly onto object-key prefixes.
/// </summary>
public sealed class MinioBlobProvider(IMinioClient client, MinioBlobOptions options) : IBlobProvider
{
    private readonly SemaphoreSlim _bucketGate = new(1, 1);
    private bool _bucketReady;

    private static string Key(string container, string blob) => $"{container}/{blob.Replace('\\', '/').TrimStart('/')}";

    private async Task EnsureBucketAsync(CancellationToken ct)
    {
        if (_bucketReady) return;
        await _bucketGate.WaitAsync(ct);
        try
        {
            if (_bucketReady) return;
            var exists = await client.BucketExistsAsync(new BucketExistsArgs().WithBucket(options.Bucket), ct);
            if (!exists && options.CreateBucketIfNotExists)
                await client.MakeBucketAsync(new MakeBucketArgs().WithBucket(options.Bucket), ct);
            _bucketReady = true;
        }
        finally
        {
            _bucketGate.Release();
        }
    }

    public async Task SaveAsync(string container, string blob, Stream stream, bool overrideExisting, CancellationToken ct)
    {
        await EnsureBucketAsync(ct);
        if (!overrideExisting && await ExistsAsync(container, blob, ct))
            throw new InvalidOperationException($"Blob '{blob}' already exists in '{container}'.");

        // MinIO needs an object size; buffer non-seekable streams to learn their length.
        Stream data = stream;
        long size;
        if (stream.CanSeek)
        {
            size = stream.Length - stream.Position;
        }
        else
        {
            var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            ms.Position = 0;
            data = ms;
            size = ms.Length;
        }

        await client.PutObjectAsync(new PutObjectArgs()
            .WithBucket(options.Bucket)
            .WithObject(Key(container, blob))
            .WithStreamData(data)
            .WithObjectSize(size)
            .WithContentType("application/octet-stream"), ct);
    }

    public async Task<Stream?> GetOrNullAsync(string container, string blob, CancellationToken ct)
    {
        await EnsureBucketAsync(ct);
        var ms = new MemoryStream();
        try
        {
            await client.GetObjectAsync(new GetObjectArgs()
                .WithBucket(options.Bucket)
                .WithObject(Key(container, blob))
                .WithCallbackStream((s, c) => s.CopyToAsync(ms, c)), ct);
        }
        catch (ObjectNotFoundException)
        {
            return null;
        }
        ms.Position = 0;
        return ms;
    }

    public async Task<bool> ExistsAsync(string container, string blob, CancellationToken ct)
    {
        await EnsureBucketAsync(ct);
        try
        {
            await client.StatObjectAsync(new StatObjectArgs()
                .WithBucket(options.Bucket)
                .WithObject(Key(container, blob)), ct);
            return true;
        }
        catch (ObjectNotFoundException)
        {
            return false;
        }
    }

    public async Task<bool> DeleteAsync(string container, string blob, CancellationToken ct)
    {
        await EnsureBucketAsync(ct);
        if (!await ExistsAsync(container, blob, ct)) return false;
        await client.RemoveObjectAsync(new RemoveObjectArgs()
            .WithBucket(options.Bucket)
            .WithObject(Key(container, blob)), ct);
        return true;
    }
}

public static class MinioBlobStoringRegistration
{
    /// <summary>Uses MinIO (S3-compatible) as the blob backend (single bucket; container = key prefix).</summary>
    public static IServiceCollection AddSliceBlobStoringMinio(
        this IServiceCollection services, Action<MinioBlobOptionsBuilder> configure)
    {
        var b = new MinioBlobOptionsBuilder();
        configure(b);
        var options = b.Build();

        var client = new MinioClient()
            .WithEndpoint(options.Endpoint)
            .WithCredentials(options.AccessKey, options.SecretKey)
            .WithSSL(options.UseSsl);
        if (!string.IsNullOrWhiteSpace(options.Region))
            client = client.WithRegion(options.Region);

        services.RemoveAll<IBlobProvider>();
        services.AddSingleton(options);
        services.AddSingleton<IMinioClient>(client.Build());
        services.AddSingleton<IBlobProvider, MinioBlobProvider>();
        return services;
    }
}

/// <summary>Mutable builder so callers configure MinIO options inline.</summary>
public sealed class MinioBlobOptionsBuilder
{
    public string Endpoint { get; set; } = "localhost:9000";
    public string AccessKey { get; set; } = "minioadmin";
    public string SecretKey { get; set; } = "minioadmin";
    public string Bucket { get; set; } = "slice";
    public bool UseSsl { get; set; }
    public string? Region { get; set; }
    public bool CreateBucketIfNotExists { get; set; } = true;

    internal MinioBlobOptions Build() => new()
    {
        Endpoint = Endpoint,
        AccessKey = AccessKey,
        SecretKey = SecretKey,
        Bucket = Bucket,
        UseSsl = UseSsl,
        Region = Region,
        CreateBucketIfNotExists = CreateBucketIfNotExists
    };
}
