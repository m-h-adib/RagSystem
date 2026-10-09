using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rag.Application.Abstractions.Embeddings;

public interface IEmbeddingService
{
	Task<EmbeddingResult> GenerateAsync(
		string text,
		CancellationToken cancellationToken = default);

	Task<IReadOnlyList<EmbeddingResult>> GenerateBatchAsync(
	   IReadOnlyList<string> texts,
	   CancellationToken cancellationToken = default);
}
