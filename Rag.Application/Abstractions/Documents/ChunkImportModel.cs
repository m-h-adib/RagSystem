using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rag.Application.Abstractions.Documents;

public sealed class ChunkImportDocument
{
	public string Name { get; set; } = string.Empty;

	public string? Description { get; set; }

	public Dictionary<string, string>? Metadata { get; set; }

	public List<ChunkImportItem> Chunks { get; set; } = [];
}

public sealed class ChunkImportItem
{
	public string Title { get; set; } = string.Empty;

	public string Text { get; set; } = string.Empty;

	public Dictionary<string, string>? Metadata { get; set; }
}
