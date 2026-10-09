using Rag.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
	  CancellationToken cancellationToken = default);
}