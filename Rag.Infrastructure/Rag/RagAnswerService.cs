using Microsoft.Extensions.Options;
using Rag.Application.Abstractions.Rag;
using Rag.Application.Abstractions.Reranking;
using Rag.Infrastructure.Ollama;
using Rag.Infrastructure.Rag.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace Rag.Infrastructure.Rag;

public sealed class RagAnswerService(
	HttpClient httpClient,
	IOptions<OllamaOptions> ollamaOptions,
	IOptions<RagOptions> ragOptions)
	: IRagAnswerService
{
	private readonly OllamaOptions _ollamaOptions = ollamaOptions.Value;
	private readonly RagOptions _ragOptions = ragOptions.Value;

	private const string NoAnswer =
		"اطلاعات کافی برای پاسخ به این سؤال در منابع موجود نیست.";

	public async Task<RagAnswerResult> GenerateAnswerAsync(
		string query,
		IReadOnlyList<RerankResult> results,
		CancellationToken cancellationToken = default)
	{
		if (results.Count == 0)
		{
			return new RagAnswerResult(NoAnswer, []);
		}

		// شماره منابع در پرامپت از 1 شروع می‌شود.
		var contextResults = results
			.Take(_ragOptions.ContextCount)
			.ToList();

		var context = string.Join(
			"\n\n",
			contextResults.Select((x, index) =>
				$"""
                [منبع {index + 1}]
                امتیاز ارتباط: {x.Score}

                عنوان:
                {x.Chunk.Title ?? ""}

                متن:
                {x.Chunk.Text}
                """));

		var prompt =
			$"""
            شما یک دستیار پرسش و پاسخ مبتنی بر منابع هستید.

            سؤال کاربر:
            {query}

            منابع:
            {context}

            وظیفه:
            فقط و فقط بر اساس منابع بالا پاسخ بده.

            قوانین پاسخ:
            1. فقط از اطلاعات موجود در منابع استفاده کن.
            2. هیچ اطلاعاتی از دانش قبلی خود اضافه نکن.
            3. هیچ عدد، نام، تاریخ، حکم یا شرطی را تغییر نده.
            4. فقط اطلاعاتی را بیان کن که مستقیماً به سؤال مربوط هستند.
            5. از تکرار متن سؤال در پاسخ خودداری کن.
            6. پاسخ باید یک جمله کامل و مستقل باشد و مستقیماً به سؤال پاسخ دهد.
            پاسخ را به یک عبارت کوتاه مانند «دو رکعتی» محدود نکن.
            اگر سؤال درباره چند مورد است، نام همه موارد را در پاسخ ذکر کن.
            7. اگر منابع ناقص هستند، اطلاعات موجود را بیان کن و چیزی حدس نزن.
            8. اگر پاسخ سؤال در منابع وجود ندارد، مقدار answer را دقیقاً برابر این عبارت قرار بده:
               {NoAnswer}
            9. اگر چند منبع اطلاعات لازم برای پاسخ را تأمین می‌کنند،
               شماره تمام آن منابع را در sourceNumbers قرار بده.
            10. منابعی را که صرفاً موضوع مشابه دارند ولی اطلاعاتی به پاسخ اضافه نمی‌کنند،
                در sourceNumbers قرار نده.
            11. شماره منابع از 1 شروع می‌شود و باید مطابق شماره درج‌شده در متن منابع باشد.
            12. اگر پاسخ در منبع 1 وجود دارد و منبع دیگری برای پاسخ لازم نیست،
                sourceNumbers باید شامل عدد 1 باشد.
            13. اگر پاسخ در هیچ منبعی وجود ندارد، sourceNumbers باید آرایه خالی باشد.
            14. پاسخ و شماره منابع باید با یکدیگر سازگار باشند.
            15. خروجی را فقط در قالب JSON مطابق ساختار تعریف‌شده برگردان.
            16. پاسخ باید بدون نیاز به مشاهده سؤال یا منابع قابل فهم باشد.
            برای مثال، به‌جای «دو رکعتی» بنویس:
            «نمازهای ظهر، عصر و عشا در سفر، در صورت وجود شرایط شرعی،
            به صورت دو رکعتی خوانده می‌شوند.»
            """;

		var request = new
		{
			model = _ollamaOptions.Model,
			stream = false,
			format = new
			{
				type = "object",
				properties = new
				{
					answer = new
					{
						type = "string"
					},
					sourceNumbers = new
					{
						type = "array",
						items = new
						{
							type = "integer"
						}
					}
				},
				required = new[]
				{
					"answer",
					"sourceNumbers"
				}
			},
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

		var ollamaResponse =
			await response.Content.ReadFromJsonAsync<OllamaResponse>(
				cancellationToken);

		if (ollamaResponse is null ||
			string.IsNullOrWhiteSpace(ollamaResponse.Message.Content))
		{
			throw new InvalidOperationException(
				"Ollama returned an empty response.");
		}

		var rawContent = ollamaResponse.Message.Content;

		Console.WriteLine("===== OLLAMA RAW RAG RESPONSE =====");
		Console.WriteLine(rawContent);
		Console.WriteLine("===================================");

		RagLlmResponse? result;

		try
		{
			result = JsonSerializer.Deserialize<RagLlmResponse>(
				rawContent,
				new JsonSerializerOptions
				{
					PropertyNameCaseInsensitive = true
				});
		}
		catch (JsonException ex)
		{
			throw new InvalidOperationException(
				$"Ollama returned invalid JSON. Raw response: {rawContent}",
				ex);
		}

		if (result is null ||
			string.IsNullOrWhiteSpace(result.Answer))
		{
			throw new InvalidOperationException(
				$"Ollama returned an invalid RAG response. Raw response: {rawContent}");
		}

		Console.WriteLine("===== SOURCE NUMBERS =====");
		Console.WriteLine(JsonSerializer.Serialize(result.SourceNumbers));

		var answer = result.Answer.Trim();

		// اگر پاسخ پیدا نشده باشد، هیچ منبعی نمایش داده نمی‌شود.
		if (answer == NoAnswer)
		{
			return new RagAnswerResult(NoAnswer, []);
		}

		// فقط شماره منابع معتبر و یکتا پذیرفته می‌شوند.
		var sourceNumbers = (result.SourceNumbers ?? [])
			.Where(number =>
				number >= 1 &&
				number <= contextResults.Count)
			.Distinct()
			.OrderBy(number => number)
			.ToList();

		return new RagAnswerResult(answer, sourceNumbers);
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
