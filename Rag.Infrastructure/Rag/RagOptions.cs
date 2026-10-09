namespace Rag.Infrastructure.Rag;

public sealed class RagOptions
{
    public int ContextCount { get; init; } = 3;

    // Number of dense-retrieval candidates sent to the reranker.
    public int CandidateCount { get; init; } = 40;

    // Keep only candidates close to the best reranker score.
    // Tune against a labeled evaluation set before treating this as a relevance threshold.
    public float ContextScoreMargin { get; init; } = 1.5f;
}
