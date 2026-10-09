using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Rag.Application.Abstractions.Embeddings;

namespace Rag.Api.Controllers;

[Route("api/embeddings")]
[ApiController]
public sealed class EmbeddingsController(
	IEmbeddingService embeddingService)
	: ControllerBase
{
	[HttpPost]
	public async Task<IActionResult> Generate(
		[FromBody] EmbeddingRequest request,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(request.Text))
		{
			return BadRequest("Text is required.");
		}

		var result = await embeddingService.GenerateAsync(
			request.Text,
			cancellationToken);

		return Ok(new
		{
			dimension = result.Vector.Count,
			vector = result.Vector
		});
	}
}

public sealed class EmbeddingRequest
{
	public string Text { get; set; } = string.Empty;
}

