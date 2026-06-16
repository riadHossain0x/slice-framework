using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Slice.BlobStoring;
using Slice.BlobStoring.Minio;
using Testcontainers.Minio;

namespace Slice.BlobStoring.Minio.Tests;

/// <summary>
/// Exercises the MinIO blob provider against a real MinIO server (Testcontainers): the full
/// save → exists → read-back → delete lifecycle, plus the override guard.
/// </summary>
public sealed class MinioBlobProviderTests : IAsyncLifetime
{
    private readonly MinioContainer _minio = new MinioBuilder().Build();
    private IBlobProvider _provider = null!;

    public async Task InitializeAsync()
    {
        await _minio.StartAsync();

        var services = new ServiceCollection();
        services.AddSliceBlobStoringMinio(o =>
        {
            o.Endpoint = $"{_minio.Hostname}:{_minio.GetMappedPublicPort(9000)}";
            o.AccessKey = _minio.GetAccessKey();
            o.SecretKey = _minio.GetSecretKey();
            o.Bucket = "slice-test";
            o.UseSsl = false;
        });
        _provider = services.BuildServiceProvider().GetRequiredService<IBlobProvider>();
    }

    public Task DisposeAsync() => _minio.DisposeAsync().AsTask();

    [Fact]
    public async Task Save_exists_read_and_delete_round_trip()
    {
        var ct = CancellationToken.None;
        var payload = Encoding.UTF8.GetBytes("hello-minio");

        Assert.False(await _provider.ExistsAsync("docs", "greeting.txt", ct));

        await _provider.SaveAsync("docs", "greeting.txt", new MemoryStream(payload), overrideExisting: false, ct);
        Assert.True(await _provider.ExistsAsync("docs", "greeting.txt", ct));

        await using (var read = await _provider.GetOrNullAsync("docs", "greeting.txt", ct))
        {
            Assert.NotNull(read);
            using var ms = new MemoryStream();
            await read!.CopyToAsync(ms, ct);
            Assert.Equal("hello-minio", Encoding.UTF8.GetString(ms.ToArray()));
        }

        // Saving again without override is rejected.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _provider.SaveAsync("docs", "greeting.txt", new MemoryStream(payload), overrideExisting: false, ct));

        Assert.True(await _provider.DeleteAsync("docs", "greeting.txt", ct));
        Assert.False(await _provider.ExistsAsync("docs", "greeting.txt", ct));
        Assert.Null(await _provider.GetOrNullAsync("docs", "greeting.txt", ct));
    }
}
