
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rag.Application.Abstractions.Documents
{
	public interface IWordDocumentParser
	{
		Task<IReadOnlyList<ParsedBlock>> ParseAsync(
			Stream documentStream,
			CancellationToken cancellationToken = default);
	}
}
