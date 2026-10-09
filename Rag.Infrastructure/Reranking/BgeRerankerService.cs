using Microsoft.Extensions.Options;
using Rag.Application.Abstractions.Reranking;
using Rag.Domain.Entities;
using Rag.Infrastructure.Embeddings;
using System.Net.Http.Json;

namespace Rag.Infrastructure.Reranking;

public sealed class BgeRerankerService(
    HttpClient httpClient,
    IOptions<BgeRerankerOptions> options) : IRerankerService
{
    private readonly BgeRerankerOptions _options = options.Value;

    public async Task<IReadOnlyList<RerankResult>> RerankAsync(
        string query,
        IReadOnlyList<Chunk> chunks,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be empty.", nameof(query));

        if (chunks.Count == 0)
            return [];

        var request = new RerankRequest
        {
            Query = query,
            Documents = chunks.Select(ChunkSearchTextBuilder.Build).ToList()
        };

        using var response = await httpClient.PostAsJsonAsync("/rerank", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<RerankResponse>(cancellationToken);
        if (result is null)
            throw new InvalidOperationException("BGE Reranker returned an empty response.");

        return result.Results
            .Where(x => x.Index >= 0 && x.Index < chunks.Count)
            .Select(x => new RerankResult(chunks[x.Index], x.Score))
            .OrderByDescending(x => x.Score)
            .ToList();
    }

    private sealed class RerankRequest
    {
        public string Query { get; init; } = string.Empty;
        public List<string> Documents { get; init; } = [];
    }

    private sealed class RerankResponse
    {
        public List<RerankItem> Results { get; init; } = [];
    }

    private sealed class RerankItem
    {
        public int Index { get; init; }
        public float Score { get; init; }
    }
}
