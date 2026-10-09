using Rag.Application.Abstractions.Embeddings;
using Rag.Application.Abstractions.VectorStore;
using Rag.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rag.Application.Abstractions.Documents;

public sealed class DocumentIngestionService(
	IWordDocumentParser wordDocumentParser,
	IDocumentStructureBuilder structureBuilder,
	IStructuredDocumentChunkingService structuredChunkingService,
	IEmbeddingService embeddingService,
	IVectorStore vectorStore)
	: IDocumentIngestionService
{
	public async Task<IReadOnlyList<Chunk>> IngestAsync(
		Document document,
		string content,
		CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException(
			"برای ingestion فایل Word از endpoint فایل استفاده کنید.");
	}

	public async Task<IReadOnlyList<Chunk>> IngestWordAsync(
		Document document,
		Stream documentStream,
		CancellationToken cancellationToken = default)
	{
		var blocks = await wordDocumentParser.ParseAsync(
			documentStream,
			cancellationToken);

		var segments = structureBuilder.Build(blocks);

		var chunks = await structuredChunkingService.CreateChunksAsync(
			document,
			segments,
			cancellationToken);

		if (chunks.Count == 0)
			return chunks;

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