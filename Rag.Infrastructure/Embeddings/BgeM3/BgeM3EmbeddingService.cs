using Microsoft.Extensions.Options;
using Rag.Application.Abstractions.Embeddings;
using System.Net.Http.Json;

namespace Rag.Infrastructure.Embeddings.BgeM3;

public sealed class BgeM3EmbeddingService(
    HttpClient httpClient,
    IOptions<BgeM3Options> options) : IEmbeddingService
{
    private readonly BgeM3Options _options = options.Value;

    public async Task<EmbeddingResult> GenerateAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text cannot be empty.", nameof(text));

        using var response = await httpClient.PostAsJsonAsync(
            "/embedding",
            new BgeM3EmbeddingRequest { Text = text },
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<BgeM3EmbeddingResponse>(cancellationToken);
        if (result is null)
            throw new InvalidOperationException("BGE-M3 returned an empty response.");

        if (result.Embedding.Count != _options.Dimension)
            throw new InvalidOperationException(
                $"Invalid embedding dimension. Expected {_options.Dimension}, received {result.Embedding.Count}.");

        return new EmbeddingResult(result.Embedding);
    }

    public async Task<IReadOnlyList<EmbeddingResult>> GenerateBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0)
            return [];

        var results = new EmbeddingResult[texts.Count];
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(_options.MaxConcurrentRequests, 1, 4),
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(
            Enumerable.Range(0, texts.Count),
            parallelOptions,
            async (index, token) =>
            {
                results[index] = await GenerateAsync(texts[index], token);
            });

        return results;
    }
}
