using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Rag.Application.Abstractions.VectorStore;
using Rag.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Rag.Infrastructure.VectorStore.Qdrant;

public sealed class QdrantVectorStore(
QdrantClient client,
IOptions<QdrantOptions> options)
: IVectorStore
{
	private readonly QdrantOptions _options = options.Value;

	public async Task UpsertChunksAsync(
		IReadOnlyList<Chunk> chunks,
		IReadOnlyList<IReadOnlyList<float>> vectors,
		CancellationToken cancellationToken = default)
	{
		if (chunks.Count != vectors.Count)
		{
			throw new ArgumentException(
				"The number of chunks must match the number of vectors.");
		}

		if (chunks.Count == 0)
			return;

		var points = new List<PointStruct>(chunks.Count);

		for (var i = 0; i < chunks.Count; i++)
		{
			var chunk = chunks[i];
			var vector = vectors[i];

			if (vector.Count != _options.VectorSize)
			{
				throw new ArgumentException(
					$"Chunk {chunk.Id}: expected {_options.VectorSize} dimensions, " +
					$"received {vector.Count}.");
			}

			if (!Guid.TryParse(chunk.Id, out var pointId))
			{
				throw new ArgumentException(
					$"Chunk ID '{chunk.Id}' must be a valid GUID.");
			}

			if (string.IsNullOrWhiteSpace(chunk.Text))
			{
				throw new ArgumentException(
					$"Chunk '{chunk.Id}' has empty text.");
			}

			var point = new PointStruct
			{
				Id = pointId,
				Vectors = vector.ToArray()
			};

			point.Payload["document_id"] = chunk.DocumentId;
			Console.WriteLine("QDRANT TEXT:");
			Console.WriteLine(chunk.Text);
			point.Payload["text"] = chunk.Text;
			point.Payload["title"] = chunk.Title ?? string.Empty;
			point.Payload["chunk_index"] = chunk.Index;
			point.Payload["metadata"] = JsonSerializer.Serialize(
				chunk.Metadata);

			points.Add(point);
		}

		await client.UpsertAsync(
			collectionName: _options.CollectionName,
			points: points,
			cancellationToken: cancellationToken);
	}

	public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
	IReadOnlyList<float> vector,
	int limit = 5,
	CancellationToken cancellationToken = default)
	{
		if (vector.Count != _options.VectorSize)
		{
			throw new ArgumentException(
				$"Invalid vector dimension. Expected {_options.VectorSize}, received {vector.Count}.");
		}

		if (limit <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(limit));
		}

		var results = await client.SearchAsync(
			collectionName: _options.CollectionName,
			vector: vector.ToArray(),
			limit: (ulong)limit,
			cancellationToken: cancellationToken);

		return results
			.Select(result =>
			{
				var payload = result.Payload;

				var chunk = new Chunk
				{
					Id = result.Id.Uuid,
					DocumentId = GetPayloadString(payload, "document_id"),
					Text = GetPayloadString(payload, "text"),
					Title = GetPayloadString(payload, "title"),
					Index = GetPayloadInt(payload, "chunk_index")
				};

				return new VectorSearchResult(
					chunk,
					result.Score);
			})
			.ToList();
	}

	private static string GetPayloadString(
	IDictionary<string, Value> payload,
	string key)
	{
		return payload.TryGetValue(key, out var value)
			? value.StringValue
			: string.Empty;
	}

	private static int GetPayloadInt(
		IDictionary<string, Value> payload,
		string key)
	{
		return payload.TryGetValue(key, out var value)
			? (int)value.IntegerValue
			: 0;
	}
}
