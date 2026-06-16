using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Slice.BlobStoring;
using Slice.BlobStoring.Postgres;

namespace Slice.Postgres.Tests;

[Collection("postgres")]
public sealed class PostgresBlobTests(PostgresFixture fx)
{
    [Fact]
    public async Task Save_exists_read_delete_round_trip()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSlicePostgresBlobStoring(fx.ConnectionString);
        using var host = builder.Build();
        await host.StartAsync();

        var provider = host.Services.GetRequiredService<IBlobProvider>();
        var payload = Encoding.UTF8.GetBytes("hello-pg-blob");

        Assert.False(await provider.ExistsAsync("docs", "a.txt", default));
        await provider.SaveAsync("docs", "a.txt", new MemoryStream(payload), overrideExisting: false, default);
        Assert.True(await provider.ExistsAsync("docs", "a.txt", default));

        await using (var read = await provider.GetOrNullAsync("docs", "a.txt", default))
        {
            Assert.NotNull(read);
            using var ms = new MemoryStream();
            await read!.CopyToAsync(ms);
            Assert.Equal("hello-pg-blob", Encoding.UTF8.GetString(ms.ToArray()));
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.SaveAsync("docs", "a.txt", new MemoryStream(payload), overrideExisting: false, default));

        Assert.True(await provider.DeleteAsync("docs", "a.txt", default));
        Assert.False(await provider.ExistsAsync("docs", "a.txt", default));

        await host.StopAsync();
    }
}
