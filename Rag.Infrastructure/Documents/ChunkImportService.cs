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

        for (var inputIndex = 0; inputIndex < input.Chunks.Count; inputIndex++)
        {
            var item = input.Chunks[inputIndex];
            if (item is null || string.IsNullOrWhiteSpace(item.Text))
                continue;

            var parts = NumberedListChunkSplitter.Split(item.Text);
            foreach (var part in parts)
            {
                var metadata = item.Metadata is null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string>(item.Metadata);

                if (part.ListItemNumber is not null)
                {
                    metadata["parentChunkIndex"] = inputIndex.ToString(
                        System.Globalization.CultureInfo.InvariantCulture);
                    metadata["listItemNumber"] = part.ListItemNumber.Value.ToString(
                        System.Globalization.CultureInfo.InvariantCulture);
                }

                var title = string.IsNullOrWhiteSpace(item.Title)
                    ? null
                    : part.ListItemNumber is null
                        ? item.Title.Trim()
                        : $"{item.Title.Trim()} — بند {part.ListItemNumber.Value}";

                chunks.Add(new Chunk
                {
                    Id = Guid.NewGuid().ToString("N"),
                    DocumentId = document.Id,
                    Text = part.Text,
                    Title = title,
                    Index = chunks.Count,
                    Metadata = metadata.Count == 0 ? null : metadata
                });
            }
        }

        if (chunks.Count == 0)
            throw new ArgumentException("هیچ Chunk معتبری در سند وجود ندارد.");

        // Use exactly the same enriched representation for every JSON-imported chunk.
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
