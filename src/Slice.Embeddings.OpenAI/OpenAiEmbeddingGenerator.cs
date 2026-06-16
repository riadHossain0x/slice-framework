using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Slice.Vector;

namespace Slice.Embeddings.OpenAI;

public sealed class OpenAiEmbeddingOptions
{
    /// <summary>API root. Works with OpenAI (<c>https://api.openai.com</c>), Azure OpenAI, Ollama
    /// (<c>http://localhost:11434</c>) and other OpenAI-compatible servers exposing <c>/v1/embeddings</c>.</summary>
    public string BaseUrl { get; set; } = "https://api.openai.com";
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "text-embedding-3-small";
    /// <summary>Embedding length the chosen model produces (e.g. 1536 for text-embedding-3-small).</summary>
    public int Dimensions { get; set; } = 1536;
}

/// <summary>
/// An <see cref="IEmbeddingGenerator"/> that calls an OpenAI-compatible <c>/v1/embeddings</c> endpoint.
/// One adapter covers OpenAI, Azure OpenAI, Ollama and LM Studio — just point <c>BaseUrl</c> at the server.
/// </summary>
public sealed class OpenAiEmbeddingGenerator(HttpClient http, OpenAiEmbeddingOptions options) : IEmbeddingGenerator
{
    public int Dimensions => options.Dimensions;

    public async Task<float[]> GenerateAsync(string text, CancellationToken ct = default)
        => (await GenerateBatchAsync([text], ct))[0];

    public async Task<IReadOnlyList<float[]>> GenerateBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.BaseUrl.TrimEnd('/')}/v1/embeddings")
        {
            Content = JsonContent.Create(new EmbeddingRequest(options.Model, texts))
        };
        if (!string.IsNullOrEmpty(options.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(ct)
            ?? throw new InvalidOperationException("Empty embeddings response.");

        // Preserve request order (the API echoes an index per item).
        return body.Data.OrderBy(d => d.Index).Select(d => d.Embedding).ToList();
    }

    private sealed record EmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] IReadOnlyList<string> Input);

    private sealed record EmbeddingResponse(
        [property: JsonPropertyName("data")] IReadOnlyList<EmbeddingData> Data);

    private sealed record EmbeddingData(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] float[] Embedding);
}

public static class OpenAiEmbeddingRegistration
{
    /// <summary>Replaces the default offline embedder with an OpenAI-compatible HTTP embedder.</summary>
    public static IServiceCollection AddSliceOpenAiEmbeddings(
        this IServiceCollection services, Action<OpenAiEmbeddingOptions> configure)
    {
        var options = new OpenAiEmbeddingOptions();
        configure(options);
        services.AddSingleton(options);
        services.RemoveAll<IEmbeddingGenerator>();
        services.AddHttpClient<IEmbeddingGenerator, OpenAiEmbeddingGenerator>();
        return services;
    }
}
