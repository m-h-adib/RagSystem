using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rag.Application.Abstractions.Documents
{
	public sealed class ParsedBlock
	{
		public required string Text { get; init; }

		public string? Style { get; init; }

		public int Level { get; init; }

		public int Order { get; init; }
	}
}
