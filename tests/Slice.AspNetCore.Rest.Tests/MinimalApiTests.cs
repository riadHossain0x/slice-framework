using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Slice.AspNetCore.Rest.Tests;

/// <summary>
/// Exercises the minimal-API edge (Slice.AspNetCore.MinimalApi): Result→IResult mapping, plus HAL and
/// version-ETag parity via endpoint filters — over the same self-hosted Kestrel host the controllers use.
/// </summary>
public sealed class MinimalApiTests(TestHost host) : IClassFixture<TestHost>
{
    [Fact]
    public async Task Get_returns_value_with_etag()
    {
        var response = await host.Client.GetAsync($"/min/widgets/{host.WidgetId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Acme", doc.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Missing_resource_maps_to_404_problem()
    {
        var response = await host.Client.GetAsync($"/min/widgets/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Widget:NotFound", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Forbidden_result_maps_to_403()
    {
        var response = await host.Client.GetAsync("/min/widgets/forbidden");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Null_value_maps_to_204()
    {
        var response = await host.Client.GetAsync("/min/widgets/none");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Validation_result_maps_to_400_with_details()
    {
        var response = await host.Client.PostAsJsonAsync("/min/widgets", new CreateWidgetBody(""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("errors").TryGetProperty("Name", out _));
    }

    [Fact]
    public async Task Hal_is_applied_on_minimal_endpoint_when_negotiated()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/min/widgets/{host.WidgetId}");
        request.Headers.Accept.ParseAdd("application/hal+json");

        var response = await host.Client.SendAsync(request);

        Assert.Equal("application/hal+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("_links").TryGetProperty("self", out _));
    }

    [Fact]
    public async Task Matching_if_none_match_returns_304()
    {
        var first = await host.Client.GetAsync($"/min/widgets/{host.WidgetId}");
        var etag = first.Headers.ETag!.ToString();

        var conditional = new HttpRequestMessage(HttpMethod.Get, $"/min/widgets/{host.WidgetId}");
        conditional.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var response = await host.Client.SendAsync(conditional);

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
    }
}
