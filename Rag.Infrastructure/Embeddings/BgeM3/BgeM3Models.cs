using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rag.Infrastructure.Embeddings.BgeM3;

public sealed class BgeM3EmbeddingRequest
{
	public required string Text { get; init; }
}

public sealed class BgeM3EmbeddingResponse
{
	public List<float> Embedding { get; init; } = [];
	public int Dimension { get; init; }
}
