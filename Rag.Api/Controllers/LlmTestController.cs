using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Rag.Application.Abstractions.Llm;

namespace Rag.Api.Controllers;

[ApiController]
[Route("api/llm-test")]
public sealed class LlmTestController(
	ILlmService llmService)
	: ControllerBase
{
	[HttpPost]
	public async Task<IActionResult> Generate(
		[FromBody] LlmTestRequest request,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(request.Prompt))
		{
			return BadRequest("Prompt is required.");
		}

		var result = await llmService.GenerateAsync(
			request.Prompt,
			cancellationToken);

		return Ok(new
		{
			answer = result
		});
	}
}

public sealed class LlmTestRequest
{
	public string Prompt { get; set; } = string.Empty;
}
