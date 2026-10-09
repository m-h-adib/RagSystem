namespace Rag.Infrastructure.Embeddings.BgeM3;

public sealed class BgeM3Options
{
    public string BaseUrl { get; init; } = "http://127.0.0.1:8000";
    public int Dimension { get; init; } = 1024;
    public int MaxConcurrentRequests { get; init; } = 2;
}
