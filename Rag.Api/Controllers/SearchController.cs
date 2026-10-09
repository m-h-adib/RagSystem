using Microsoft.AspNetCore.Builder.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Rag.Application.Abstractions.Embeddings;
using Rag.Application.Abstractions.Rag;
using Rag.Application.Abstractions.Reranking;
using Rag.Application.Abstractions.VectorStore;
using Rag.Infrastructure.Rag;

namespace Rag.Api.Controllers;

[ApiController]
[Route("api/search")]
public sealed class SearchController(
	IEmbeddingService embeddingService,
	IVectorStore vectorStore,
	IRerankerService rerankerService,
	IRagAnswerService ragAnswerService,
	IOptions<RagOptions> ragOptions)
	: ControllerBase
{
	private readonly RagOptions _ragOptions = ragOptions.Value;

	[HttpPost]
	public async Task<IActionResult> Search(
		[FromBody] SearchRequest request,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(request.Query))
		{
			return BadRequest("Query is required.");
		}

		var finalLimit = Math.Clamp(request.Limit, 1, 20);

		// 1. Generate embedding
		var embedding = await embeddingService.GenerateAsync(
			request.Query,
			cancellationToken);

		// 2. Semantic search
		// بیشتر از تعداد نهایی می‌گیریم تا Reranker انتخاب کند.
		var vectorResults = await vectorStore.SearchAsync(
			embedding.Vector,
			limit: Math.Max(10, finalLimit),
			cancellationToken);


		return Ok(vectorResults);

		if (vectorResults.Count == 0)
		{
			return Ok(new
			{
				answer = "اطلاعات کافی برای پاسخ به این سؤال در منابع موجود نیست.",
				sources = Array.Empty<object>()
			});
		}

		// 3. Rerank
		var rerankedResults = await rerankerService.RerankAsync(
			request.Query,
			vectorResults.Select(x => x.Chunk).ToList(),
			cancellationToken);

		var relevantResults = rerankedResults
			.Where(x => x.Score >= _ragOptions.MinRelevanceScore)
			.Take(_ragOptions.ContextCount)
			.ToList();

		if (relevantResults.Count == 0)
		{
			return Ok(new
			{
				answer = "اطلاعات کافی برای پاسخ به این سؤال در منابع موجود نیست.",
				sources = Array.Empty<object>()
			});
		}

		var ragResult = await ragAnswerService.GenerateAnswerAsync(
			request.Query,
			relevantResults,
			cancellationToken);

		// شماره منابع انتخاب‌شده توسط مدل، یک‌مبنا است.
		// این شماره‌ها به همان فهرست contextResults اشاره دارند.
		var contextResults = relevantResults
			.Take(_ragOptions.ContextCount)
			.ToList();

		var sources = ragResult.SourceNumbers
			.Where(number =>
				number >= 1 &&
				number <= contextResults.Count)
			.Select(number => contextResults[number - 1])
			.Take(finalLimit)
			.Select(x => new
			{
				score = x.Score,
				id = x.Chunk.Id,
				documentId = x.Chunk.DocumentId,
				title = x.Chunk.Title,
				text = x.Chunk.Text,
				index = x.Chunk.Index
			})
			.ToList();

		return Ok(new
		{
			answer = ragResult.Answer,
			sources
		});
	}
}

public sealed class SearchRequest
{
	public string Query { get; set; } = string.Empty;

	public int Limit { get; set; } = 5;
}
