using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rag.Application.Abstractions.Documents
{
	public interface IDocumentStructureBuilder
	{
		IReadOnlyList<StructuredSegment> Build(
			IReadOnlyList<ParsedBlock> blocks);
	}
}
