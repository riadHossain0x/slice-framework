using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Slice.BlobStoring;

namespace Slice.BlobStoring.Azure;

/// <summary>Azure Blob Storage backend. Each Slice container maps to an Azure container.</summary>
public sealed class AzureBlobProvider(BlobServiceClient serviceClient) : IBlobProvider
{
    public async Task SaveAsync(string container, string blob, Stream stream, bool overrideExisting, CancellationToken ct)
    {
        var client = await GetContainerAsync(container, ct);
        await client.GetBlobClient(blob).UploadAsync(stream, overwrite: overrideExisting, ct);
    }

    public async Task<Stream?> GetOrNullAsync(string container, string blob, CancellationToken ct)
    {
        var client = (await GetContainerAsync(container, ct)).GetBlobClient(blob);
        if (!await client.ExistsAsync(ct))
            return null;
        var download = await client.DownloadStreamingAsync(cancellationToken: ct);
        return download.Value.Content;
    }

    public async Task<bool> ExistsAsync(string container, string blob, CancellationToken ct)
        => await (await GetContainerAsync(container, ct)).GetBlobClient(blob).ExistsAsync(ct);

    public async Task<bool> DeleteAsync(string container, string blob, CancellationToken ct)
        => await (await GetContainerAsync(container, ct)).GetBlobClient(blob).DeleteIfExistsAsync(cancellationToken: ct);

    private async Task<BlobContainerClient> GetContainerAsync(string container, CancellationToken ct)
    {
        var client = serviceClient.GetBlobContainerClient(container);
        await client.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        return client;
    }
}

public static class AzureBlobStoringRegistration
{
    /// <summary>Uses Azure Blob Storage as the blob backend.</summary>
    public static IServiceCollection AddSliceBlobStoringAzure(this IServiceCollection services, string connectionString)
    {
        services.RemoveAll<IBlobProvider>();
        services.AddSingleton(new BlobServiceClient(connectionString));
        services.AddSingleton<IBlobProvider, AzureBlobProvider>();
        return services;
    }
}
