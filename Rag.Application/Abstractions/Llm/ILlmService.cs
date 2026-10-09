using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rag.Application.Abstractions.Llm;

public interface ILlmService
{
	Task<string> GenerateAsync(
		string prompt,
		CancellationToken cancellationToken = default);
}
