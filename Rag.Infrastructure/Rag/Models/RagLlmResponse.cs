using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Rag.Infrastructure.Rag.Models
{
	public sealed class RagLlmResponse
	{
		[JsonPropertyName("answer")]
		public string Answer { get; set; } = string.Empty;

		[JsonPropertyName("sourceNumbers")]
		public List<int> SourceNumbers { get; set; } = [];
	}
}
