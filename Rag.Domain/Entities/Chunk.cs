using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rag.Domain.Entities;

public sealed class Chunk
{
	public required string Id { get; init; }

	public required string DocumentId { get; init; }

	public required string Text { get; init; }

	public string? Title { get; init; }

	public int Index { get; init; }

	public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}