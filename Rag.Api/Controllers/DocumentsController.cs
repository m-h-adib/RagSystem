using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Rag.Application.Abstractions.Documents;

namespace Rag.Api.Controllers;

[ApiController]
[Route("api/documents")]
public sealed class DocumentsController(IChunkImportService chunkImportService)
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

        if (!string.Equals(
                Path.GetExtension(file.FileName),
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

        try
        {
            var result = await chunkImportService.ImportAsync(input, cancellationToken);
            return Ok(new
            {
                documentId = result.Document.Id,
                documentName = result.Document.Name,
                chunkCount = result.Chunks.Count
            });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }
}
