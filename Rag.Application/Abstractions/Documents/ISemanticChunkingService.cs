using Rag.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rag.Application.Abstractions.Documents;

public interface ISemanticChunkingService
{
	Task<IReadOnlyList<Chunk>> CreateChunksAsync(
		Document document,
		string content,
		CancellationToken cancellationToken = default);
}
