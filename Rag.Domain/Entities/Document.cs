using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rag.Domain.Entities;

public sealed class Document
{
	public required string Id { get; init; }

	public required string Name { get; init; }

	public string? Description { get; init; }

	public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
