using System.Net;
using System.Text;
using Slice.Embeddings.OpenAI;

namespace Slice.Embeddings.OpenAI.Tests;

/// <summary>Unit tests for the OpenAI-compatible embedder — no network: a stub handler stands in for
/// the <c>/v1/embeddings</c> endpoint, so we verify request shaping and response parsing.</summary>
public sealed class OpenAiEmbeddingTests
{
    private sealed class StubHandler(string responseJson) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }

    [Fact]
    public async Task Generates_embeddings_and_preserves_request_order()
    {
        // Response intentionally out of order to prove we sort by index.
        const string json = """
            { "data": [
                { "index": 1, "embedding": [0.4, 0.5, 0.6] },
                { "index": 0, "embedding": [0.1, 0.2, 0.3] }
            ] }
            """;
        var stub = new StubHandler(json);
        var options = new OpenAiEmbeddingOptions { BaseUrl = "https://example.test", ApiKey = "sk-test", Model = "text-embedding-3-small", Dimensions = 3 };
        var generator = new OpenAiEmbeddingGenerator(new HttpClient(stub), options);

        var result = await generator.GenerateBatchAsync(["first", "second"]);

        Assert.Equal(3, generator.Dimensions);
        Assert.Equal(new[] { 0.1f, 0.2f, 0.3f }, result[0]);   // index 0
        Assert.Equal(new[] { 0.4f, 0.5f, 0.6f }, result[1]);   // index 1

        Assert.Equal(HttpMethod.Post, stub.LastRequest!.Method);
        Assert.EndsWith("/v1/embeddings", stub.LastRequest.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer sk-test", stub.LastRequest.Headers.Authorization!.ToString());
        Assert.Contains("text-embedding-3-small", stub.LastBody);
        Assert.Contains("first", stub.LastBody);
        Assert.Contains("second", stub.LastBody);
    }

    [Fact]
    public async Task Single_generate_returns_the_first_vector()
    {
        const string json = """{ "data": [ { "index": 0, "embedding": [1.0, 2.0] } ] }""";
        var generator = new OpenAiEmbeddingGenerator(
            new HttpClient(new StubHandler(json)),
            new OpenAiEmbeddingOptions { Dimensions = 2 });

        Assert.Equal(new[] { 1.0f, 2.0f }, await generator.GenerateAsync("hello"));
    }
}
