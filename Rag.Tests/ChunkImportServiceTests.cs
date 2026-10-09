using Rag.Application.Abstractions.Embeddings;
using Rag.Application.Abstractions.Documents;
using Rag.Application.Abstractions.VectorStore;
using Rag.Domain.Entities;
using Rag.Infrastructure.Documents;

namespace Rag.Tests;

public sealed class ChunkImportServiceTests
{
    [Fact]
    public async Task ImportAsync_PreservesEachValidJsonChunkWithoutSplittingOrRewritingText()
    {
        const string numberedText = "1. حکم اول\n2. حکم دوم";
        var input = new ChunkImportDocument
        {
            Name = "کتاب آزمایشی",
            Metadata = new Dictionary<string, string>
            {
                ["book"] = "فقه",
                ["edition"] = "1405"
            },
            Chunks =
            [
                new ChunkImportItem
                {
                    Title = "  احکام نماز  ",
                    Text = numberedText,
                    Metadata = new Dictionary<string, string>
                    {
                        ["section"] = "نماز"
                    }
                },
                new ChunkImportItem
                {
                    Title = "طهارت",
                    Text = "متن چانک دوم"
                },
                new ChunkImportItem
                {
                    Title = "چانک خالی",
                    Text = "   "
                }
            ]
        };

        var embeddings = new FakeEmbeddingService();
        var vectorStore = new FakeVectorStore();
        var service = new ChunkImportService(embeddings, vectorStore);

        var result = await service.ImportAsync(input);

        Assert.Equal(2, result.Chunks.Count);
        Assert.Equal(numberedText, result.Chunks[0].Text);
        Assert.Equal("احکام نماز", result.Chunks[0].Title);
        Assert.Equal("متن چانک دوم", result.Chunks[1].Text);
        Assert.Equal("فقه", result.Chunks[0].Metadata!["document.book"]);
        Assert.Equal("1405", result.Chunks[0].Metadata!["document.edition"]);
        Assert.Equal("نماز", result.Chunks[0].Metadata!["section"]);
        Assert.Equal(2, embeddings.ReceivedTexts.Count);
        Assert.Equal(2, vectorStore.StoredChunks.Count);
        Assert.Equal(2, vectorStore.StoredVectors.Count);
    }

    private sealed class FakeEmbeddingService : IEmbeddingService
    {
        public List<string> ReceivedTexts { get; } = [];

        public Task<EmbeddingResult> GenerateAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            ReceivedTexts.Add(text);
            return Task.FromResult(new EmbeddingResult([0.1f, 0.2f]));
        }

        public Task<IReadOnlyList<EmbeddingResult>> GenerateBatchAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken = default)
        {
            ReceivedTexts.AddRange(texts);
            IReadOnlyList<EmbeddingResult> result = texts
                .Select(_ => new EmbeddingResult([0.1f, 0.2f]))
                .ToList();

            return Task.FromResult(result);
        }
    }

    private sealed class FakeVectorStore : IVectorStore
    {
        public IReadOnlyList<Chunk> StoredChunks { get; private set; } = [];
        public IReadOnlyList<IReadOnlyList<float>> StoredVectors { get; private set; } = [];

        public Task UpsertChunksAsync(
            IReadOnlyList<Chunk> chunks,
            IReadOnlyList<IReadOnlyList<float>> vectors,
            CancellationToken cancellationToken = default)
        {
            StoredChunks = chunks.ToList();
            StoredVectors = vectors.ToList();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
            IReadOnlyList<float> vector,
            int limit = 5,
            string? documentId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<VectorSearchResult>>([]);
    }
}
