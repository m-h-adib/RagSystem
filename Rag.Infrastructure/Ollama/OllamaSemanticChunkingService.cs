using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Rag.Application.Abstractions.Documents;
using Rag.Domain.Entities;
using Rag.Infrastructure.Ollama.Models;

namespace Rag.Infrastructure.Ollama;

public sealed class OllamaSemanticChunkingService(
	HttpClient httpClient,
	IOptions<OllamaOptions> options)
	: ISemanticChunkingService
{
	private readonly OllamaOptions _options = options.Value;

	public async Task<IReadOnlyList<Chunk>> CreateChunksAsync(
		Document document,
		string content,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(content))
		{
			return [];
		}

		var request = new
		{
			model = _options.Model,
			stream = false,

			format = GetResponseSchema(),

			messages = new[]
			{
				new
				{
					role = "system",
					content = SemanticChunkPrompt.System
				},
				new
				{
					role = "user",
					content
				}
			}
		};

		var json = JsonSerializer.Serialize(request);

		using var requestContent = new StringContent(
			json,
			System.Text.Encoding.UTF8,
			"application/json");

		using var response = await httpClient.PostAsync(
			"/api/chat",
			requestContent,
			cancellationToken);

		response.EnsureSuccessStatusCode();

		var responseJson = await response.Content.ReadAsStringAsync(
			cancellationToken);

		var ollamaResponse =
			JsonSerializer.Deserialize<OllamaChatResponse>(
				responseJson,
				new JsonSerializerOptions
				{
					PropertyNameCaseInsensitive = true
				});

		if (ollamaResponse is null ||
			string.IsNullOrWhiteSpace(ollamaResponse.Message.Content))
		{
			throw new InvalidOperationException(
				"Ollama returned an empty response.");
		}

		Console.WriteLine("========== OLLAMA RAW RESPONSE ==========");
		Console.WriteLine(ollamaResponse.Message.Content);
		Console.WriteLine("==========================================");

		var result =
			JsonSerializer.Deserialize<SemanticChunkResponse>(
				ollamaResponse.Message.Content,
				new JsonSerializerOptions
				{
					PropertyNameCaseInsensitive = true
				});

		if (result is null)
		{
			throw new InvalidOperationException(
				"Could not deserialize Ollama semantic chunk response.");
		}

		Console.WriteLine("OLLAMA CHUNK TEXT:");

		foreach (var item in result.Chunks)
		{
			Console.WriteLine(item.Text);
		}

		return result.Chunks
			.Where(x => !string.IsNullOrWhiteSpace(x.Text))
			.Select((x, index) => new Chunk
			{
				Id = Guid.NewGuid().ToString("N"),
				DocumentId = document.Id,
				Index = index,
				Title = x.Title.Trim(),
				Text = x.Text.Trim()
			})
			.ToList();
	}

	private static object GetResponseSchema()
	{
		return new
		{
			type = "object",

			properties = new
			{
				chunks = new
				{
					type = "array",

					items = new
					{
						type = "object",

						properties = new
						{
							title = new
							{
								type = "string"
							},

							text = new
							{
								type = "string"
							}
						},

						required = new[]
						{
							"title",
							"text"
						}
					}
				}
			},

			required = new[]
			{
				"chunks"
			}
		};
	}

	private sealed class OllamaChatResponse
	{
		public OllamaMessage Message { get; set; } = new();
	}

	private sealed class OllamaMessage
	{
		public string Content { get; set; } = string.Empty;
	}
}