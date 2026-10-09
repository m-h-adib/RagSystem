using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rag.Infrastructure.Ollama.Models;

public sealed class SemanticChunkResponse
{
	public List<SemanticChunkItem> Chunks { get; set; } = [];
}

public sealed class SemanticChunkItem
{
	public string Title { get; set; } = string.Empty;
	public string Text { get; set; } = string.Empty;
}