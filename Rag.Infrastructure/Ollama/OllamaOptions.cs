using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rag.Infrastructure.Ollama;

public sealed class OllamaOptions
{
	public string BaseUrl { get; init; } = "http://localhost:11434";

	public string Model { get; init; } = "llama3.1:8b";
}
