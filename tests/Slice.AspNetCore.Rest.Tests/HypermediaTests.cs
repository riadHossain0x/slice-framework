using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Slice.AspNetCore.Rest.Tests;

public sealed class HypermediaTests(TestHost host) : IClassFixture<TestHost>
{
    [Fact]
    public async Task Hal_single_resource_has_self_and_permitted_links()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/widgets/{host.WidgetId}");
        request.Headers.Accept.ParseAdd("application/hal+json");
        request.Headers.Add("X-Granted", WidgetLinks.EditPermission);

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/hal+json", response.Content.Headers.ContentType?.MediaType);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        // Original payload is preserved alongside the hypermedia.
        Assert.Equal("Acme", root.GetProperty("name").GetString());

        var links = root.GetProperty("_links");
        Assert.True(links.TryGetProperty("self", out var self));
        Assert.Contains($"/api/widgets/{host.WidgetId}", self.GetProperty("href").GetString());

        Assert.True(links.TryGetProperty("update", out var update));
        Assert.Equal("PATCH", update.GetProperty("method").GetString());
    }

    [Fact]
    public async Task Hal_hides_link_when_permission_is_not_granted()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/widgets/{host.WidgetId}");
        request.Headers.Accept.ParseAdd("application/hal+json");
        // No X-Granted header → the caller lacks widget.edit.

        var response = await host.Client.SendAsync(request);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var links = doc.RootElement.GetProperty("_links");

        Assert.True(links.TryGetProperty("self", out _));    // unconditional link still present
        Assert.False(links.TryGetProperty("update", out _)); // permission-gated link hidden
    }

    [Fact]
    public async Task Hal_resolves_named_route_and_literal_href_links()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/widgets/{host.WidgetId}");
        request.Headers.Accept.ParseAdd("application/hal+json");

        var response = await host.Client.SendAsync(request);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var links = doc.RootElement.GetProperty("_links");

        // Named route (GetUriByName) → absolute URL with the {id} substituted.
        var audit = links.GetProperty("audit").GetProperty("href").GetString();
        Assert.EndsWith($"/api/widgets/{host.WidgetId}/audit", audit);

        // Literal href → emitted verbatim.
        Assert.Equal("https://docs.example/widgets", links.GetProperty("docs").GetProperty("href").GetString());
    }

    [Fact]
    public async Task Hal_collection_uses_embedded_with_custom_rel()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/widgets");
        request.Headers.Accept.ParseAdd("application/hal+json");

        var response = await host.Client.SendAsync(request);

        Assert.Equal("application/hal+json", response.Content.Headers.ContentType?.MediaType);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.True(root.GetProperty("_links").TryGetProperty("self", out _));
        var items = root.GetProperty("_embedded").GetProperty("widgets");
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.True(items.GetArrayLength() >= 1);
        Assert.True(items[0].GetProperty("_links").TryGetProperty("self", out _));
    }

    [Fact]
    public async Task Plain_json_is_unchanged_when_hal_not_requested()
    {
        // Default Accept (application/json) → no hypermedia, byte-for-byte the original DTO shape.
        var response = await host.Client.GetAsync($"/api/widgets/{host.WidgetId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual("application/hal+json", response.Content.Headers.ContentType?.MediaType);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(doc.RootElement.TryGetProperty("_links", out _));
        Assert.True(doc.RootElement.TryGetProperty("concurrencyStamp", out _));
    }
}
