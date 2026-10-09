using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Rag.Application.Abstractions.Documents;
using Rag.Domain.Entities;

namespace Rag.Api.Controllers;

[Route("api/semantic-chunking")]
[ApiController]
public sealed class SemanticChunkingController(
ISemanticChunkingService chunkingService)
: ControllerBase
{
	[HttpPost]
	public async Task<IActionResult> CreateChunks(
		[FromBody] SemanticChunkingRequest request,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(request.Content))
		{
			return BadRequest("Content is required.");
		}

		var document = new Document
		{
			Id = Guid.NewGuid().ToString("N"),
			Name = request.Name
		};

		var chunks = await chunkingService.CreateChunksAsync(
			document,
			request.Content,
			cancellationToken);

		return Ok(chunks);
	}
}

public sealed class SemanticChunkingRequest
{
	public string Name { get; set; } = "Test Document";

	public string Content { get; set; } = string.Empty;
}
