using Rag.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rag.Application.Abstractions.Reranking;

public interface IRerankerService
{
	Task<IReadOnlyList<RerankResult>> RerankAsync(
		string query,
		IReadOnlyList<Chunk> chunks,
		CancellationToken cancellationToken = default);
}
