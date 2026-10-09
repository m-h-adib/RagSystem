using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rag.Application.Abstractions.Embeddings;


public sealed record EmbeddingResult(
	IReadOnlyList<float> Vector
);
