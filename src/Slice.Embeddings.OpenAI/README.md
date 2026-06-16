# Slice.Embeddings.OpenAI

> An OpenAI-compatible HTTP embedding generator for the Slice `IEmbeddingGenerator` seam.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md), the [docs](../../docs/) and the [PostgreSQL stack guide](../../docs/postgresql-stack.md).

## Overview

`Slice.Embeddings.OpenAI` replaces the offline default embedder from `Slice.Vector` with a real one that calls an OpenAI-compatible `POST {BaseUrl}/v1/embeddings` endpoint. Because that wire format is widely supported, the same adapter works against OpenAI, Azure OpenAI, Ollama and LM Studio — you just point `BaseUrl` at the server. There is no provider SDK: it uses a typed `HttpClient` and `System.Net.Http.Json`, sending the model plus a batch of input strings and reading back embeddings ordered by the API-supplied index.

## Dependencies

- **Slice:** `Slice.Vector`
- **Third-party:** `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Http`

## Registration

```csharp
services.AddSliceOpenAiEmbeddings(o =>
{
    o.ApiKey = builder.Configuration["OpenAi:ApiKey"];
    o.Model = "text-embedding-3-small";
    o.Dimensions = 1536;
    // o.BaseUrl defaults to https://api.openai.com
});
```

`AddSliceOpenAiEmbeddings` builds and configures an `OpenAiEmbeddingOptions`, registers it as a singleton, calls `RemoveAll<IEmbeddingGenerator>()` to drop the default `HashingEmbeddingGenerator`, then registers `OpenAiEmbeddingGenerator` as a typed `HttpClient` client (`AddHttpClient<IEmbeddingGenerator, OpenAiEmbeddingGenerator>()`).

## Key types

| Type | Kind | Description |
|---|---|---|
| `OpenAiEmbeddingOptions` | sealed class | `BaseUrl` (default `https://api.openai.com`), `ApiKey` (nullable), `Model` (default `text-embedding-3-small`), `Dimensions` (default `1536`). |
| `OpenAiEmbeddingGenerator` | sealed class (`IEmbeddingGenerator`) | `(HttpClient, OpenAiEmbeddingOptions)`; POSTs to `/v1/embeddings`; `Dimensions`, `GenerateAsync`, `GenerateBatchAsync`. |
| `OpenAiEmbeddingRegistration` | static class | Hosts `AddSliceOpenAiEmbeddings(this IServiceCollection, Action<OpenAiEmbeddingOptions>)`. |

## Usage

```csharp
// OpenAI
services.AddSliceOpenAiEmbeddings(o =>
{
    o.ApiKey = "sk-...";
    o.Model = "text-embedding-3-small";
    o.Dimensions = 1536;
});

// Ollama (local, no key)
services.AddSliceOpenAiEmbeddings(o =>
{
    o.BaseUrl = "http://localhost:11434";
    o.Model = "nomic-embed-text";
    o.Dimensions = 768;
});

// Resolve and use anywhere IEmbeddingGenerator is injected:
public sealed class Embedder(IEmbeddingGenerator generator)
{
    public Task<float[]> EmbedAsync(string text, CancellationToken ct) => generator.GenerateAsync(text, ct);
}
```

## Notes

- **Drop-in replacement.** Registration removes the default `IEmbeddingGenerator` (`HashingEmbeddingGenerator`) before adding this one, so anything depending on `IEmbeddingGenerator` transparently gets the HTTP-backed implementation.
- **Set `Dimensions` to match the model.** The value is returned verbatim from `IEmbeddingGenerator.Dimensions` and is used to size vector collections; it must equal the embedding length the chosen model produces (e.g. 1536 for `text-embedding-3-small`).
- **Auth is optional.** A `Bearer {ApiKey}` header is sent only when `ApiKey` is non-empty — convenient for keyless local servers like Ollama/LM Studio.
- **Batch ordering.** `GenerateBatchAsync` sends all inputs in one request and reorders results by the `index` the API echoes; `GenerateAsync` is a single-item batch.
- `BaseUrl` has its trailing slash trimmed before `/v1/embeddings` is appended. The typed `HttpClient` is created via `Microsoft.Extensions.Http`; the generator's effective lifetime follows that client. A non-success status throws (`EnsureSuccessStatusCode`) and an empty body throws `InvalidOperationException`.
