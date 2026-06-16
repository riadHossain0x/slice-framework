using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Slice.AspNetCore.Rest.Tests;

public sealed class ConditionalRequestTests(TestHost host) : IClassFixture<TestHost>
{
    [Fact]
    public async Task Get_returns_strong_etag()
    {
        var response = await host.Client.GetAsync($"/api/widgets/{host.WidgetId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);
        Assert.False(response.Headers.ETag!.IsWeak);
    }

    [Fact]
    public async Task Matching_if_none_match_returns_304_without_body()
    {
        var first = await host.Client.GetAsync($"/api/widgets/{host.WidgetId}");
        var etag = first.Headers.ETag!.ToString();

        var conditional = new HttpRequestMessage(HttpMethod.Get, $"/api/widgets/{host.WidgetId}");
        conditional.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var response = await host.Client.SendAsync(conditional);

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Non_matching_if_none_match_returns_200()
    {
        var conditional = new HttpRequestMessage(HttpMethod.Get, $"/api/widgets/{host.WidgetId}");
        conditional.Headers.TryAddWithoutValidation("If-None-Match", "\"not-the-current-version\"");
        var response = await host.Client.SendAsync(conditional);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Stale_if_match_returns_412_then_current_if_match_succeeds()
    {
        // Read the current version.
        var current = await host.Client.GetAsync($"/api/widgets/{host.WidgetId}");
        var currentEtag = current.Headers.ETag!.ToString();

        // A stale precondition → 412 (and the write is not applied).
        var stale = new HttpRequestMessage(HttpMethod.Patch, $"/api/widgets/{host.WidgetId}/name")
        {
            Content = JsonContent.Create(new ChangeNameRequest("Renamed"))
        };
        stale.Headers.Add("X-Granted", WidgetLinks.EditPermission);
        stale.Headers.TryAddWithoutValidation("If-Match", "\"stale-version\"");
        var staleResponse = await host.Client.SendAsync(stale);
        Assert.Equal(HttpStatusCode.PreconditionFailed, staleResponse.StatusCode);

        // The current precondition → 200, and the returned version has rolled.
        var ok = new HttpRequestMessage(HttpMethod.Patch, $"/api/widgets/{host.WidgetId}/name")
        {
            Content = JsonContent.Create(new ChangeNameRequest("Renamed"))
        };
        ok.Headers.Add("X-Granted", WidgetLinks.EditPermission);
        ok.Headers.TryAddWithoutValidation("If-Match", currentEtag);
        var okResponse = await host.Client.SendAsync(ok);

        Assert.Equal(HttpStatusCode.OK, okResponse.StatusCode);
        using var doc = JsonDocument.Parse(await okResponse.Content.ReadAsStringAsync());
        var newStamp = doc.RootElement.GetProperty("concurrencyStamp").GetString();
        Assert.NotEqual(currentEtag.Trim('"'), newStamp);
        Assert.Equal("Renamed", doc.RootElement.GetProperty("name").GetString());
    }
}
