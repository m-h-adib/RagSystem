using Rag.Application.Abstractions.Documents;
using Rag.Application.Abstractions.Embeddings;
using Rag.Application.Abstractions.VectorStore;
using Rag.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rag.Infrastructure.Documents;

public sealed class WordDocumentIngestionService(
IWordDocumentParser wordDocumentParser,
IDocumentStructureBuilder structureBuilder,
IStructuredDocumentChunkingService structuredChunkingService,
IEmbeddingService embeddingService,
IVectorStore vectorStore)
: IWordDocumentIngestionService
{
	public async Task<IReadOnlyList<Chunk>> IngestAsync(
		Document document,
		Stream documentStream,
		CancellationToken cancellationToken = default)
	{
		var blocks = await wordDocumentParser.ParseAsync(
			documentStream,
			cancellationToken);

		if (blocks.Count == 0)
			return [];

		var segments = structureBuilder.Build(blocks);

		if (segments.Count == 0)
			return [];

		var chunks = await structuredChunkingService.CreateChunksAsync(
			document,
			segments,
			cancellationToken);

		if (chunks.Count == 0)
			return [];

		var texts = chunks
			.Select(x => x.Text)
			.ToList();

		var embeddings = await embeddingService.GenerateBatchAsync(
			texts,
			cancellationToken);

		await vectorStore.UpsertChunksAsync(
			chunks,
			embeddings.Select(x => x.Vector).ToList(),
			cancellationToken);

		return chunks;
	}
}
