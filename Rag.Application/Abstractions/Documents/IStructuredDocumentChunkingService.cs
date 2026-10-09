using Rag.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rag.Application.Abstractions.Documents
{
	public interface IStructuredDocumentChunkingService
	{
		Task<IReadOnlyList<Chunk>> CreateChunksAsync(
			Document document,
			IReadOnlyList<StructuredSegment> segments,
			CancellationToken cancellationToken = default);
	}
}
