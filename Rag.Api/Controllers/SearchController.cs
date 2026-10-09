using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Rag.Application.Abstractions.Embeddings;
using Rag.Application.Abstractions.Rag;
using Rag.Application.Abstractions.Reranking;
using Rag.Application.Abstractions.VectorStore;
using Rag.Infrastructure.Rag;

namespace Rag.Api.Controllers;

[ApiController]
[Route("api/search")]
public sealed class SearchController(
    IEmbeddingService embeddingService,
    IVectorStore vectorStore,
    IRerankerService rerankerService,
    IRagAnswerService ragAnswerService,
    IOptions<RagOptions> ragOptions)
    : ControllerBase
{
    private readonly RagOptions _ragOptions = ragOptions.Value;
    private const string NoAnswer =
        "اطلاعات کافی برای پاسخ به این سؤال در منابع موجود نیست.";

    [HttpPost]
    public async Task<IActionResult> Search(
        [FromBody] SearchRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return BadRequest("Query is required.");

        var finalLimit = Math.Clamp(request.Limit, 1, 20);
        var candidateCount = Math.Clamp(
            _ragOptions.CandidateCount,
            Math.Max(10, finalLimit),
            100);

        var embedding = await embeddingService.GenerateAsync(
            request.Query.Trim(),
            cancellationToken);

        var vectorResults = await vectorStore.SearchAsync(
            embedding.Vector,
            limit: candidateCount,
            documentId: string.IsNullOrWhiteSpace(request.DocumentId)
                ? null
                : request.DocumentId.Trim(),
            cancellationToken: cancellationToken);

        Console.WriteLine(
            $"[RAG DEBUG] Vector results: {vectorResults.Count}");

        if (vectorResults.Count == 0)
            return Ok(new { answer = NoAnswer, sources = Array.Empty<object>() });

        var rerankedResults = await rerankerService.RerankAsync(
            request.Query.Trim(),
            vectorResults.Select(x => x.Chunk).ToList(),
            cancellationToken);

        Console.WriteLine(
            $"[RAG DEBUG] Reranker scores: " +
            $"{string.Join(", ", rerankedResults.Select(x => x.Score))}");

        Console.WriteLine(
            $"[RAG DEBUG] Score min: " +
            $"{(rerankedResults.Count > 0 ? rerankedResults.Min(x => x.Score) : 0)}, " +
            $"max: " +
            $"{(rerankedResults.Count > 0 ? rerankedResults.Max(x => x.Score) : 0)}");

        // Cross-encoder reranker scores are raw logits and may all be negative.
        // Use relative ranking instead of an uncalibrated absolute score threshold.
        var relevantResults = rerankedResults
            .OrderByDescending(x => x.Score)
            .Take(Math.Clamp(_ragOptions.ContextCount, 1, 20))
            .ToList();

        Console.WriteLine(
            $"[RAG DEBUG] Reranked results: {rerankedResults.Count}");
        Console.WriteLine(
            $"[RAG DEBUG] Context results: {relevantResults.Count}, " +
            $"top scores: {string.Join(", ", relevantResults.Select(x => x.Score))}");

        if (relevantResults.Count == 0)
            return Ok(new { answer = NoAnswer, sources = Array.Empty<object>() });

        var ragResult = await ragAnswerService.GenerateAnswerAsync(
            request.Query.Trim(),
            relevantResults,
            cancellationToken);

        var contextResults = relevantResults
            .Take(_ragOptions.ContextCount)
            .ToList();

        var sources = ragResult.SourceNumbers
            .Where(number => number >= 1 && number <= contextResults.Count)
            .Distinct()
            .Select(number => contextResults[number - 1])
            .Take(finalLimit)
            .Select(x => new
            {
                score = x.Score,
                id = x.Chunk.Id,
                documentId = x.Chunk.DocumentId,
                title = x.Chunk.Title,
                text = x.Chunk.Text,
                index = x.Chunk.Index,
                metadata = x.Chunk.Metadata
            })
            .ToList();

        return Ok(new { answer = ragResult.Answer, sources });
    }
}

public sealed class SearchRequest
{
    public string Query { get; set; } = string.Empty;
    public int Limit { get; set; } = 5;

    // Optional: restrict retrieval to a single imported document.
    public string? DocumentId { get; set; }
}
