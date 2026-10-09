using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rag.Infrastructure.VectorStore.Qdrant;

public sealed class QdrantOptions
{
	public string Host { get; init; } = "localhost";
	public int GrpcPort { get; init; } = 6334;
	public string CollectionName { get; init; } = "rag_chunks_v3";
	public int VectorSize { get; init; } = 1024;
}
