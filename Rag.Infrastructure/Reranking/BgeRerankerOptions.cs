using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rag.Infrastructure.Reranking;

public sealed class BgeRerankerOptions
{
	public string BaseUrl { get; init; } = "http://127.0.0.1:8001";
}
