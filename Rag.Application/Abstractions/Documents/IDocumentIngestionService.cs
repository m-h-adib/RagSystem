using Rag.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rag.Application.Abstractions.Documents;

public interface IDocumentIngestionService
{
	Task<IReadOnlyList<Chunk>> IngestAsync(
		Document document,
		string content,
		CancellationToken cancellationToken = default);
}
