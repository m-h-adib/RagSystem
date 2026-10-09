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

        if (input.Chunks is null || input.Chunks.Count == 0)
            throw new ArgumentException("سند حداقل باید یک Chunk داشته باشد.");

        var document = new Document
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = input.Name.Trim(),
            Description = input.Description,
            Metadata = input.Metadata
        };

        var chunks = new List<Chunk>();

        foreach (var item in input.Chunks)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Text))
                continue;

            // JSON is the source of truth: one valid input chunk becomes one stored chunk.
            // Only metadata is enriched; chunk text and title are not split or rewritten.
            var metadata = new Dictionary<string, string>();
            if (input.Metadata is not null)
            {
                foreach (var pair in input.Metadata)
                    metadata[$"document.{pair.Key}"] = pair.Value;
            }

            if (item.Metadata is not null)
            {
                foreach (var pair in item.Metadata)
                    metadata[pair.Key] = pair.Value;
            }

            chunks.Add(new Chunk
            {
                Id = Guid.NewGuid().ToString("N"),
                DocumentId = document.Id,
                Text = item.Text,
                Title = string.IsNullOrWhiteSpace(item.Title) ? null : item.Title.Trim(),
                Index = chunks.Count,
                Metadata = metadata.Count == 0 ? null : metadata
            });
        }

        if (chunks.Count == 0)
            throw new ArgumentException("هیچ Chunk معتبری در سند وجود ندارد.");

        // Use the same enriched representation for every JSON-imported chunk.
        var embeddingTexts = chunks.Select(ChunkSearchTextBuilder.Build).ToList();
        var embeddings = await embeddingService.GenerateBatchAsync(
            embeddingTexts,
            cancellationToken);

        if (embeddings.Count != chunks.Count)
            throw new InvalidOperationException(
                $"Embedding count ({embeddings.Count}) does not match chunk count ({chunks.Count}).");

        await vectorStore.UpsertChunksAsync(
            chunks,
            embeddings.Select(x => x.Vector).ToList(),
            cancellationToken);

        return (document, chunks);
    }
}
