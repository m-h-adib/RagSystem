using Rag.Domain.Entities;

namespace Rag.Application.Abstractions.VectorStore;

public interface IVectorStore
{
    Task UpsertChunksAsync(
        IReadOnlyList<Chunk> chunks,
        IReadOnlyList<IReadOnlyList<float>> vectors,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        IReadOnlyList<float> vector,
        int limit = 5,
        string? documentId = null,
        CancellationToken cancellationToken = default);
}
