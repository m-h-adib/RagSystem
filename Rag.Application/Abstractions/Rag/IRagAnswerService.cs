using Rag.Application.Abstractions.Reranking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rag.Application.Abstractions.Rag;

public interface IRagAnswerService
{
	Task<RagAnswerResult> GenerateAnswerAsync(
		string query,
		IReadOnlyList<RerankResult> results,
		CancellationToken cancellationToken = default);
}
