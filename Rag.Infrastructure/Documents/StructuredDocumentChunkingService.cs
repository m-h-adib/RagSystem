using Rag.Application.Abstractions.Documents;
using Rag.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rag.Infrastructure.Documents
{
	public sealed class StructuredDocumentChunkingService(
	ISemanticChunkingService semanticChunkingService)
	: IStructuredDocumentChunkingService
	{
		public async Task<IReadOnlyList<Chunk>> CreateChunksAsync(
			Document document,
			IReadOnlyList<StructuredSegment> segments,
			CancellationToken cancellationToken = default)
		{
			var result = new List<Chunk>();

			foreach (var segment in segments.OrderBy(x => x.Order))
			{
				cancellationToken.ThrowIfCancellationRequested();

				if (string.IsNullOrWhiteSpace(segment.Text))
					continue;

				var chunks = await semanticChunkingService.CreateChunksAsync(
					document,
					segment.Text,
					cancellationToken);

				foreach (var chunk in chunks)
				{
					result.Add(new Chunk
					{
						Id = chunk.Id,
						DocumentId = document.Id,
						Text = chunk.Text,
						Title = chunk.Title,
						Index = result.Count,
						Metadata = segment.Metadata
					});
				}
			}

			return result;
		}
	}
}
