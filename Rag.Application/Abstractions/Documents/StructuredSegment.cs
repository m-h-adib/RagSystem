using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rag.Application.Abstractions.Documents
{
	public sealed class StructuredSegment
	{
		public required string Text { get; init; }

		public IReadOnlyDictionary<string, string>? Metadata { get; init; }

		public int Order { get; init; }
	}
}
