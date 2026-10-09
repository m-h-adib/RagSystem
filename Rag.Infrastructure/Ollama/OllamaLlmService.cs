using Microsoft.Extensions.Options;
using Rag.Application.Abstractions.Llm;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;

namespace Rag.Infrastructure.Ollama;

public sealed class OllamaLlmService(
	HttpClient httpClient,
	IOptions<OllamaOptions> options) : ILlmService
{
	private readonly OllamaOptions _options = options.Value;

	public async Task<string> GenerateAsync(
		string prompt,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(prompt))
		{
			throw new ArgumentException(
				"Prompt cannot be empty.",
				nameof(prompt));
		}

		var request = new
		{
			model = _options.Model,
			stream = false,
			messages = new[]
			{
				new
				{
					role = "user",
					content = prompt
				}
			}
		};

		using var response = await httpClient.PostAsJsonAsync(
			"/api/chat",
			request,
			cancellationToken);

		response.EnsureSuccessStatusCode();

		var result =
			await response.Content.ReadFromJsonAsync<OllamaResponse>(
				cancellationToken);

		if (result is null ||
			string.IsNullOrWhiteSpace(result.Message.Content))
		{
			throw new InvalidOperationException(
				"Ollama returned an empty response.");
		}

		return result.Message.Content.Trim();
	}

	private sealed class OllamaResponse
	{
		public OllamaMessage Message { get; set; } = new();
	}

	private sealed class OllamaMessage
	{
		public string Content { get; set; } = string.Empty;
	}
}
