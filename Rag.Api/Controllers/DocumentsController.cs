using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Rag.Application.Abstractions.Documents;
using Rag.Domain.Entities;
using Rag.Infrastructure.Documents;
using System.Text.Json;

namespace Rag.Api.Controllers;

[ApiController]
[Route("api/documents")]
public sealed class DocumentsController(
	IDocumentIngestionService ingestionService,
	 IWordDocumentParser _wordDocumentParser,
	 IDocumentStructureBuilder _structureBuilder,
	 IWordDocumentIngestionService _wordDocumentIngestionService,
	 IChunkImportService _chunkImportService)
	: ControllerBase
{

	[HttpPost("import-chunks")]
	[Consumes("multipart/form-data")]
	public async Task<IActionResult> ImportChunks(
	IFormFile file,
	CancellationToken cancellationToken)
	{
		if (file is null || file.Length == 0)
			return BadRequest("فایل JSON معتبر نیست.");

		var extension = Path.GetExtension(file.FileName);

		if (!string.Equals(
				extension,
				".json",
				StringComparison.OrdinalIgnoreCase))
		{
			return BadRequest("فقط فایل JSON قابل قبول است.");
		}

		ChunkImportDocument? input;

		await using var stream = file.OpenReadStream();

		try
		{
			input = await JsonSerializer.DeserializeAsync<ChunkImportDocument>(
				stream,
				new JsonSerializerOptions
				{
					PropertyNameCaseInsensitive = true
				},
				cancellationToken);
		}
		catch (JsonException)
		{
			return BadRequest("ساختار JSON معتبر نیست.");
		}

		if (input is null)
			return BadRequest("محتوای JSON خالی است.");

		var result = await _chunkImportService.ImportAsync(
			input,
			cancellationToken);

		return Ok(new
		{
			documentId = result.Document.Id,
			documentName = result.Document.Name,
			chunkCount = result.Chunks.Count
		});
	}

	[HttpPost("ingest")]
	public async Task<IActionResult> Ingest(
		[FromBody] IngestDocumentRequest request,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(request.Name))
		{
			return BadRequest("Document name is required.");
		}

		if (string.IsNullOrWhiteSpace(request.Content))
		{
			return BadRequest("Document content is required.");
		}

		var document = new Document
		{
			Id = Guid.NewGuid().ToString("N"),
			Name = request.Name,
			Description = request.Description,
			Metadata = request.Metadata
		};

		var chunks = await ingestionService.IngestAsync(
			document,
			request.Content,
			cancellationToken);

		return Ok(new
		{
			document.Id,
			document.Name,
			chunkCount = chunks.Count,
			chunks
		});
	}

	[HttpPost("parse")]
	public async Task<IActionResult> Parse(
	IFormFile file,
	CancellationToken cancellationToken)
	{
		if (file is null || file.Length == 0)
			return BadRequest("فایل نامعتبر است.");

		await using var stream = file.OpenReadStream();

		var blocks = await _wordDocumentParser.ParseAsync(
			stream,
			cancellationToken);

		return Ok(new
		{
			total = blocks.Count,
			first = blocks.Take(10),
			last = blocks.TakeLast(10)
		});
	}



	[HttpPost("ingest-word")]
	[Consumes("multipart/form-data")]
	public async Task<IActionResult> IngestWord(
	IFormFile file,
	CancellationToken cancellationToken)
	{
		if (file is null || file.Length == 0)
			return BadRequest("فایل Word معتبر نیست.");

		var extension = Path.GetExtension(file.FileName);

		if (!string.Equals(
				extension,
				".docx",
				StringComparison.OrdinalIgnoreCase))
		{
			return BadRequest("فقط فایل DOCX قابل قبول است.");
		}

		var document = new Document
		{
			Id = Guid.NewGuid().ToString("N"),
			Name = Path.GetFileNameWithoutExtension(file.FileName),
			Description = null,
			Metadata = new Dictionary<string, string>
			{
				["source"] = "word",
				["fileName"] = file.FileName
			}
		};

		await using var stream = file.OpenReadStream();

		var chunks = await _wordDocumentIngestionService.IngestAsync(
			document,
			stream,
			cancellationToken);

		return Ok(new
		{
			id = document.Id,
			name = document.Name,
			chunkCount = chunks.Count,
			chunks
		});
	}


	[HttpPost("parse-structure")]
	public async Task<IActionResult> ParseStructure(
	IFormFile file,
	CancellationToken cancellationToken)
	{
		if (file is null || file.Length == 0)
			return BadRequest("فایل نامعتبر است.");

		await using var stream = file.OpenReadStream();

		var blocks = await _wordDocumentParser.ParseAsync(
			stream,
			cancellationToken);

		var segments = _structureBuilder.Build(blocks);

		return Ok(new
		{
			blockCount = blocks.Count,
			segmentCount = segments.Count,
			first = segments.Take(10),
			last = segments.TakeLast(10)
		});
	}



}

public sealed class IngestDocumentRequest
{
	public string Name { get; set; } = string.Empty;

	public string? Description { get; set; }

	public string Content { get; set; } = string.Empty;

	public Dictionary<string, string>? Metadata { get; set; }
}
