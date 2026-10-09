using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rag.Infrastructure.Embeddings.BgeM3;

public sealed class BgeM3Options
{
	public string BaseUrl { get; init; } = "http://127.0.0.1:8000";
	public int Dimension { get; init; } = 1024;
}
