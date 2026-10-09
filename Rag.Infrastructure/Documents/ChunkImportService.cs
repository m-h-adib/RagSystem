using Rag.Application.Abstractions.Documents;
using Rag.Application.Abstractions.Embeddings;
using Rag.Application.Abstractions.VectorStore;
using Rag.Domain.Entities;
using Rag.Infrastructure.Embeddings;

namespace Rag.Infrastructure.Documents;

public sealed class ChunkImportService(
    IEmbeddingService embeddingService,
    IVectorStore vectorStore) : IChunkImportService
{
    public async Task<(Document Document, IReadOnlyList<Chunk> Chunks)> ImportAsync(
        ChunkImportDocument input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (string.IsNullOrWhiteSpace(input.Name))
            throw new ArgumentException("نام سند الزامی است.");

        if (input.Chunks.Count == 0)
            throw new ArgumentException("سند حداقل باید یک Chunk داشته باشد.");

        var document = new Document
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = input.Name.Trim(),
            Description = input.Description,
            Metadata = input.Metadata
        };

        var chunks = input.Chunks
            .Where(x => !string.IsNullOrWhiteSpace(x.Text))
            .Select((x, index) => new Chunk
            {
                Id = Guid.NewGuid().ToString("N"),
                DocumentId = document.Id,
                Text = x.Text.Trim(),
                Title = string.IsNullOrWhiteSpace(x.Title) ? null : x.Title.Trim(),
                Index = index,
                Metadata = x.Metadata
            })
            .ToList();

        if (chunks.Count == 0)
            throw new ArgumentException("هیچ Chunk معتبری در سند وجود ندارد.");

        // Titles and section metadata must be embedded with the body text.
        var embeddingTexts = chunks.Select(ChunkSearchTextBuilder.Build).ToList();
        var embeddings = await embeddingService.GenerateBatchAsync(embeddingTexts, cancellationToken);

        await vectorStore.UpsertChunksAsync(
            chunks,
            embeddings.Select(x => x.Vector).ToList(),
            cancellationToken);

        return (document, chunks);
    }
}
