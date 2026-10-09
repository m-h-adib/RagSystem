using Rag.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rag.Application.Abstractions.VectorStore;

public sealed record VectorSearchResult(
	Chunk Chunk,
	float Score
);