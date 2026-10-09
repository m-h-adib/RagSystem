using Microsoft.Extensions.Options;
using Rag.Application.Abstractions.Rag;
using Rag.Application.Abstractions.Reranking;
using Rag.Infrastructure.Ollama;
using Rag.Infrastructure.Rag.Models;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

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

        Console.WriteLine("[RAG DEBUG] Context passed to Ollama:");
        Console.WriteLine(context);
        Console.WriteLine("[RAG DEBUG] End of Ollama context.");

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
            7. پیش از پاسخ، سؤال را به بخش‌های اصلی و شرط‌های مشخص آن تقسیم کن و بررسی کن که متن منابع مستقیماً هر بخش لازم را پشتیبانی می‌کند یا نه.
            8. شباهت موضوعی کافی نیست؛ منبع باید خودِ حکم یا شرط مورد سؤال را بیان کند. اطلاعات درباره موضوعی نزدیک، مثال مشابه، یا وضعیت دیگری را به‌جای پاسخ سؤال ارائه نکن.
            9. اگر حتی یک شرط اصلی سؤال در منابع ذکر نشده یا از آن‌ها قابل استنتاج مستقیم نیست، پاسخ کامل سؤال در منابع موجود نیست؛ در این حالت هیچ پاسخ جزئی یا حدسی نده و مقدار answer را دقیقاً برابر این عبارت قرار بده:
               {NoAnswer}
            10. هرگاه از قانون بالا استفاده کردی، sourceNumbers باید آرایه خالی باشد.
            11. فقط وقتی پاسخ بده که منبع، پاسخ مستقیم و کافی به سؤال ارائه می‌کند. برای مثال، اگر سؤال درباره «هشت فرسخ» است، متنی صرفاً درباره اقامت ده‌روزه در مکه یا نماز در مشاعر پاسخ آن سؤال محسوب نمی‌شود.
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
            17. اگر یک منبع چند حکم، نظر، گروه یا استثنای متفاوت دارد، هر گروه را جداگانه بیان کن.
            18. هر نام را دقیقاً در همان گروهی نگه دار که در منبع آمده است؛ نام‌ها را بین گروه‌ها جابه‌جا نکن.
            19. عبارت‌های متفاوت مانند «مانعی ندارد»، «معفو نیست» و «بنا بر احتیاط واجب معفو نیست» را یکی نکن و به‌جای هم به کار نبر.
            20. اگر منبع چند دسته را با شماره یا عبارت جداکننده مشخص کرده است، ساختار دسته‌بندی و نسبت هر حکم به نام‌ها را حفظ کن؛ در صورت نیاز پاسخ را به صورت فهرست بنویس.
            21. برای خلاصه‌کردن، چند حکم متفاوت را به یک حکم کلی تبدیل نکن؛ حتی اگر پاسخ طولانی‌تر شود، همه دسته‌های مرتبط را جداگانه و دقیق گزارش کن.
            22. پیش از نهایی‌کردن پاسخ، تطبیق بده که هر نام و حکم در پاسخ دقیقاً با همان نام و حکم در منبع مطابقت داشته باشد.
            23. وقتی منبع برای هر حکم، نام اشخاص یا مراجع را ذکر کرده است، آوردن عبارت‌هایی مثل «برخی از مراجع» کافی نیست؛ نام تمام افراد هر گروه مرتبط را صریحاً در پاسخ بیاور.
            24. اگر متن منبع به شکل چند بند شماره‌دار است، پاسخ را نیز با بندهای جداگانه بنویس و در هر بند هم حکم و هم تمام نام‌های همان بند را ذکر کن.
            25. پاسخ را آن‌قدر خلاصه نکن که اطلاعات اصلی منبع، به‌ویژه نام‌ها، شرط‌ها یا استثناها حذف شوند.
            26. برای سؤال درباره نظر مراجع، پاسخ باید مشخص کند دقیقاً کدام مرجع چه نظری دارد؛ اگر متن منبع نام‌ها را گروه‌بندی کرده، همان گروه‌بندی را حفظ کن.
            نمونهٔ ساختار پاسخ برای منبعی با چند نظر:
            «۱. [حکم اول]: [تمام نام‌های گروه اول].
            ۲. [حکم دوم]: [تمام نام‌های گروه دوم].
            ۳. [حکم سوم]: [تمام نام‌های گروه سوم].»
            این نمونه فقط قالب پاسخ است؛ اطلاعات واقعی را فقط از متن منابع استخراج کن.
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

		// Never return a generated answer that cannot be tied to at least one
		// valid context source. This is a citation-integrity guard, not proof
		// that the selected source actually supports every claim.
		if (sourceNumbers.Count == 0)
		{
			Console.WriteLine("[RAG DEBUG] No valid source numbers returned; abstaining.");
			return new RagAnswerResult(NoAnswer, []);
		}

		foreach (var sourceNumber in sourceNumbers)
		{
			if (TryBuildGroupedOpinionAnswer(contextResults[sourceNumber - 1].Chunk.Text, out var groupedAnswer))
			{
				answer = groupedAnswer;
				break;
			}
		}

		return new RagAnswerResult(answer, sourceNumbers);
	}


	private static bool TryBuildGroupedOpinionAnswer(string sourceText, out string answer)
	{
		answer = string.Empty;
		if (string.IsNullOrWhiteSpace(sourceText))
		{
			return false;
		}

		var text = sourceText.Replace('ي', 'ی').Replace('ك', 'ک');
		var matches = Regex.Matches(
			text,
			@"(?m)^\s*\d+\s*[\p{P}\p{Cf}]*\s*(?<ruling>.*?)[؛;]\s*آیات\s+عظام\s*:\s*(?<names>[^\r\n]+?)\s*\.?\s*$",
			RegexOptions.CultureInvariant);

		if (matches.Count < 2)
		{
			return false;
		}

		var groups = new List<string>(matches.Count);
		foreach (Match match in matches)
		{
			var ruling = match.Groups["ruling"].Value.Trim().TrimEnd('؛', ';', '.', ' ');
			var names = match.Groups["names"].Value.Trim().TrimEnd('.', ' ');
			if (string.IsNullOrWhiteSpace(ruling) || string.IsNullOrWhiteSpace(names))
			{
				return false;
			}

			groups.Add($"{groups.Count + 1}. {ruling}: {names}.");
		}

		answer = string.Join(Environment.NewLine, groups);
		return true;
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
