using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Rag.Application.Abstractions.Embeddings;
using Rag.Application.Abstractions.VectorStore;
using Rag.Domain.Entities;

namespace Rag.Api.Controllers;

[Route("api/vector-store")]
public sealed class VectorStoreController(
IEmbeddingService embeddingService,
IVectorStore vectorStore)
: ControllerBase
{
	[HttpPost("test")]
	public async Task<IActionResult> Test(
		[FromBody] VectorStoreTestRequest request,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(request.Text))
		{
			return BadRequest("Text is required.");
		}

		var chunk = new Chunk
		{
			Id = Guid.NewGuid().ToString("N"),
			DocumentId = Guid.NewGuid().ToString("N"),
			Text = request.Text,
			Title = request.Title,
			Index = 0
		};

		var embedding = await embeddingService.GenerateAsync(
			chunk.Text,
			cancellationToken);

		await vectorStore.UpsertChunksAsync(
			[chunk],
			[embedding.Vector],
			cancellationToken);

		return Ok(new
		{
			chunk.Id,
			chunk.DocumentId,
			chunk.Title,
			chunk.Text,
			vectorDimension = embedding.Vector.Count
		});
	}
}

public sealed class VectorStoreTestRequest
{
	public string Text { get; set; } = string.Empty;

	public string? Title { get; set; }
}
