using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Slice.BlobStoring;

namespace Slice.BlobStoring.Aws;

public sealed class AwsS3BlobOptions
{
    public required string Bucket { get; init; }
}

/// <summary>AWS S3 backend. Blobs are keyed <c>{container}/{blob}</c> within a single bucket.</summary>
public sealed class AwsS3BlobProvider(IAmazonS3 s3, AwsS3BlobOptions options) : IBlobProvider
{
    private string Key(string container, string blob) => $"{container}/{blob.Replace('\\', '/').TrimStart('/')}";

    public async Task SaveAsync(string container, string blob, Stream stream, bool overrideExisting, CancellationToken ct)
    {
        if (!overrideExisting && await ExistsAsync(container, blob, ct))
            throw new InvalidOperationException($"Blob '{blob}' already exists in '{container}'.");

        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = options.Bucket,
            Key = Key(container, blob),
            InputStream = stream,
            AutoCloseStream = false
        }, ct);
    }

    public async Task<Stream?> GetOrNullAsync(string container, string blob, CancellationToken ct)
    {
        try
        {
            var response = await s3.GetObjectAsync(options.Bucket, Key(container, blob), ct);
            var ms = new MemoryStream();
            await response.ResponseStream.CopyToAsync(ms, ct);
            ms.Position = 0;
            return ms;
        }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<bool> ExistsAsync(string container, string blob, CancellationToken ct)
    {
        try
        {
            await s3.GetObjectMetadataAsync(options.Bucket, Key(container, blob), ct);
            return true;
        }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<bool> DeleteAsync(string container, string blob, CancellationToken ct)
    {
        if (!await ExistsAsync(container, blob, ct)) return false;
        await s3.DeleteObjectAsync(options.Bucket, Key(container, blob), ct);
        return true;
    }
}

public static class AwsBlobStoringRegistration
{
    /// <summary>Uses AWS S3 as the blob backend (single bucket; container becomes the key prefix).</summary>
    public static IServiceCollection AddSliceBlobStoringAws(
        this IServiceCollection services, string bucket, IAmazonS3? client = null)
    {
        services.RemoveAll<IBlobProvider>();
        services.AddSingleton(new AwsS3BlobOptions { Bucket = bucket });
        if (client is not null)
            services.AddSingleton(client);
        else
            services.TryAddSingleton<IAmazonS3>(_ => new AmazonS3Client());
        services.AddSingleton<IBlobProvider, AwsS3BlobProvider>();
        return services;
    }
}
