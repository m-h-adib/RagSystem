using Rag.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rag.Application.Abstractions.Reranking;

public sealed record RerankResult(
	Chunk Chunk,
	float Score);
