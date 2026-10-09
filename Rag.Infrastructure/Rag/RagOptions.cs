namespace Rag.Infrastructure.Rag;

public sealed class RagOptions
{
    public int ContextCount { get; init; } = 3;

    // Number of dense-retrieval candidates sent to the reranker.
    public int CandidateCount { get; init; } = 40;

    // Keep aligned with the configured reranker model's score distribution.
    public float MinRelevanceScore { get; init; } = 0;
}
