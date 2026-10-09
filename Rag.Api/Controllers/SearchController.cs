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

        Console.WriteLine($"[RAG DEBUG] Query: {request.Query.Trim()}");

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
        Console.WriteLine("[RAG DEBUG] Top vector candidates:");
        foreach (var item in vectorResults.Take(10))
        {
            Console.WriteLine(
                $"[RAG DEBUG] VECTOR score={item.Score}, index={item.Chunk.Index}, " +
                $"title={item.Chunk.Title}, text={Preview(item.Chunk.Text, 500)}");
        }

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

        var rankedResults = rerankedResults
            .OrderByDescending(x => x.Score)
            .ToList();

        Console.WriteLine("[RAG DEBUG] Top reranked candidates:");
        foreach (var item in rankedResults.Take(10))
        {
            Console.WriteLine(
                $"[RAG DEBUG] RERANK score={item.Score}, index={item.Chunk.Index}, " +
                $"title={item.Chunk.Title}, text={Preview(item.Chunk.Text, 500)}");
        }

        Console.WriteLine($"[RAG DEBUG] Reranked results: {rankedResults.Count}");

        // Cross-encoder scores are ranking signals, not calibrated probabilities.
        // Exclude candidates that fall far below the best result for this query.
        // The margin is configurable and must be evaluated against labeled queries.
        var contextCount = Math.Clamp(_ragOptions.ContextCount, 1, 20);
        var scoreMargin = Math.Max(0f, _ragOptions.ContextScoreMargin);
        var topScore = rankedResults.Count > 0 ? rankedResults[0].Score : float.NegativeInfinity;
        var scoreFloor = topScore - scoreMargin;

        var relevantResults = rankedResults
            .Where(x => x.Score >= scoreFloor)
            .Take(contextCount)
            .ToList();

        Console.WriteLine(
            $"[RAG DEBUG] Context selection: topScore={topScore}, " +
            $"scoreMargin={scoreMargin}, scoreFloor={scoreFloor}, " +
            $"contextCount={relevantResults.Count}, " +
            $"selected scores: {string.Join(", ", relevantResults.Select(x => x.Score))}");

        Console.WriteLine("[RAG DEBUG] Exact context selected for answer generation:");
        foreach (var item in relevantResults)
        {
            Console.WriteLine(
                $"[RAG DEBUG] CONTEXT score={item.Score}, index={item.Chunk.Index}, " +
                $"title={item.Chunk.Title}, text={Preview(item.Chunk.Text, 1200)}");
        }

        if (relevantResults.Count == 0)
            return Ok(new { answer = NoAnswer, sources = Array.Empty<object>() });

        var ragResult = await ragAnswerService.GenerateAnswerAsync(
            request.Query.Trim(),
            relevantResults,
            cancellationToken);

        // The answer service receives the already-filtered context, so source numbers
        // must map to this same ordered list.
        var contextResults = relevantResults;

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

    private static string Preview(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var normalized = value.Replace("\r", " ").Replace("\n", " ");
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength] + "...";
    }
}

public sealed class SearchRequest
{
    public string Query { get; set; } = string.Empty;
    public int Limit { get; set; } = 5;

    // Optional: restrict retrieval to a single imported document.
    public string? DocumentId { get; set; }
}
