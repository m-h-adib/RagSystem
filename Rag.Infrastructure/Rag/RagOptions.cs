using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rag.Infrastructure.Rag;

public sealed class RagOptions
{
	public int ContextCount { get; init; } = 3;

	public float MinRelevanceScore { get; init; } = 0;
}
