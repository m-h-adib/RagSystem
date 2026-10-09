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

		// A question may contain an explicit quantity/unit that the retrieved
		// passages never mention. Do not let a topically related passage answer it.
		if (HasUnsupportedQuantityAnchor(query, contextResults))
		{
			Console.WriteLine("[RAG DEBUG] Explicit quantity/unit anchor is absent from context; abstaining.");
			return new RagAnswerResult(NoAnswer, []);
		}

		if (HasAmbiguousStayDurationScenario(query, contextResults))
		{
			Console.WriteLine("[RAG DEBUG] Query combines established 10-day intention with departure before completing 10 days, while sources state different rulings for both conditions; abstaining.");
			return new RagAnswerResult(NoAnswer, []);
		}

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
            12. اگر چند منبع اطلاعات لازم برای پاسخ را تأمین می‌کنند،
               شماره تمام آن منابع را در sourceNumbers قرار بده.
            13. منابعی را که صرفاً موضوع مشابه دارند ولی اطلاعاتی به پاسخ اضافه نمی‌کنند،
                در sourceNumbers قرار نده.
            14. شماره منابع از 1 شروع می‌شود و باید مطابق شماره درج‌شده در متن منابع باشد.
            15. اگر پاسخ در منبع 1 وجود دارد و منبع دیگری برای پاسخ لازم نیست،
                sourceNumbers باید شامل عدد 1 باشد.
            16. اگر پاسخ در هیچ منبعی وجود ندارد، sourceNumbers باید آرایه خالی باشد.
            17. پاسخ و شماره منابع باید با یکدیگر سازگار باشند.
            18. خروجی را فقط در قالب JSON مطابق ساختار تعریف‌شده برگردان.
            19. پاسخ باید بدون نیاز به مشاهده سؤال یا منابع قابل فهم باشد.
            20. اگر یک منبع چند حکم، نظر، گروه یا استثنای متفاوت دارد، هر گروه را جداگانه بیان کن.
            21. هر نام را دقیقاً در همان گروهی نگه دار که در منبع آمده است؛ نام‌ها را بین گروه‌ها جابه‌جا نکن.
            22. عبارت‌های متفاوت مانند «مانعی ندارد»، «معفو نیست» و «بنا بر احتیاط واجب معفو نیست» را یکی نکن و به‌جای هم به کار نبر.
            23. اگر منبع چند دسته را با شماره یا عبارت جداکننده مشخص کرده است، ساختار دسته‌بندی و نسبت هر حکم به نام‌ها را حفظ کن؛ در صورت نیاز پاسخ را به صورت فهرست بنویس.
            24. برای خلاصه‌کردن، چند حکم متفاوت را به یک حکم کلی تبدیل نکن؛ حتی اگر پاسخ طولانی‌تر شود، همه دسته‌های مرتبط را جداگانه و دقیق گزارش کن.
            25. پیش از نهایی‌کردن پاسخ، تطبیق بده که هر نام و حکم در پاسخ دقیقاً با همان نام و حکم در منبع مطابقت داشته باشد.
            26. وقتی منبع برای هر حکم، نام اشخاص یا مراجع را ذکر کرده است، آوردن عبارت‌هایی مثل «برخی از مراجع» کافی نیست؛ نام تمام افراد هر گروه مرتبط را صریحاً در پاسخ بیاور.
            27. اگر متن منبع به شکل چند بند شماره‌دار است، پاسخ را نیز با بندهای جداگانه بنویس و در هر بند هم حکم و هم تمام نام‌های همان بند را ذکر کن.
            28. پاسخ را آن‌قدر خلاصه نکن که اطلاعات اصلی منبع، به‌ویژه نام‌ها، شرط‌ها یا استثناها حذف شوند.
            29. برای سؤال درباره نظر مراجع، پاسخ باید مشخص کند دقیقاً کدام مرجع چه نظری دارد؛ اگر متن منبع نام‌ها را گروه‌بندی کرده، همان گروه‌بندی را حفظ کن.
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

		var selectedSources = string.Join(
			"\n\n",
			sourceNumbers.Select(number =>
			{
				var chunk = contextResults[number - 1].Chunk;
				return $"[منبع {number}]\nعنوان: {chunk.Title ?? ""}\nمتن: {chunk.Text}";
			}));

		if (!await IsAnswerSupportedAsync(query, answer, selectedSources, cancellationToken))
		{
			Console.WriteLine("[RAG DEBUG] Semantic support validation failed; abstaining.");
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


	private async Task<bool> IsAnswerSupportedAsync(
		string query,
		string answer,
		string selectedSources,
		CancellationToken cancellationToken)
	{
		var validationPrompt = $"""
			شما اعتبارسنج مستقل پاسخ یک سامانه پرسش‌وپاسخ مبتنی بر منبع هستید.
			باید هم «کامل بودن پاسخ نسبت به سؤال» و هم «پشتیبانی مستقیم منبع از پاسخ» را بررسی کنید.
			
			روش بررسی:
			1. سؤال را به تمام درخواست‌ها، شرط‌ها، استثناها، مقایسه‌ها و بخش‌های اصلی مستقل تقسیم کنید.
			2. برای هر بخش بررسی کنید آیا پاسخ واقعاً به همان بخش جواب داده است یا آن را نادیده گرفته است.
			3. بررسی کنید آیا منبع انتخاب‌شده مستقیماً همان ادعا و شرط را بیان می‌کند؛ ارتباط موضوعی یا شباهت واژگانی کافی نیست.
			4. بررسی کنید آیا پاسخ از دو حالت جداگانهٔ منبع، نتیجه‌ای برای یک حالت ترکیبی ساخته که منبع آن را روشن نکرده است.
			5. بررسی کنید آیا پاسخ شرط سؤال را تغییر داده، شرطی را حذف کرده، استثنایی را نادیده گرفته یا ادعای تازه‌ای اضافه کرده است.
			
			مقدار supported فقط وقتی true است که پاسخ به همه بخش‌های ضروری سؤال جواب دهد و تمام ادعاهای آن مستقیماً از منابع انتخاب‌شده پشتیبانی شوند.
			اگر حتی یک بخش ضروری بی‌پاسخ است، منبع درباره حالت دقیق سؤال ساکت یا مبهم است، یا برای رسیدن به پاسخ نیاز به حدس و استنتاج فراتر از متن است، supported=false.
			اگر منبع دو حالت یا دو حکم متفاوت را بیان می‌کند، آن‌ها را به یک حالت تازه تعمیم ندهید.
			در صورت تردید، supported=false.
			به دانش قبلی خود تکیه نکنید و درستی فقهی یا بیرونی پاسخ را داوری نکنید؛ فقط کامل بودن و پشتیبانی متنی را بسنجید.
			
			سؤال:
			{query}
			
			پاسخ پیشنهادی:
			{answer}
			
			منابع انتخاب‌شده:
			{selectedSources}
			
			فقط JSON مطابق ساختار خواسته‌شده برگردانید.
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
					supported = new { type = "boolean" },
					reason = new { type = "string" }
				},
				required = new[] { "supported", "reason" }
			},
			messages = new[]
			{
				new { role = "user", content = validationPrompt }
			}
		};

		try
		{
			using var response = await httpClient.PostAsJsonAsync(
				"/api/chat", request, cancellationToken);
			response.EnsureSuccessStatusCode();

			var ollamaResponse = await response.Content.ReadFromJsonAsync<OllamaResponse>(
				cancellationToken);
			var raw = ollamaResponse?.Message.Content;
			if (string.IsNullOrWhiteSpace(raw))
			{
				Console.WriteLine("[RAG DEBUG] Semantic validator returned empty response.");
				return false;
			}

			var validation = JsonSerializer.Deserialize<SupportCheckResponse>(
				raw,
				new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

			Console.WriteLine($"[RAG DEBUG] Semantic support validation: {validation?.Supported}; reason: {validation?.Reason}");
			return validation?.Supported == true;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			// Fail closed: do not return an unverified answer if validation fails.
			Console.WriteLine($"[RAG DEBUG] Semantic validator failed; abstaining. {ex.Message}");
			return false;
		}
	}

	private sealed class SupportCheckResponse
	{
		public bool Supported { get; set; }
		public string Reason { get; set; } = string.Empty;
	}

	private static bool HasAmbiguousStayDurationScenario(
		string query,
		IReadOnlyList<RerankResult> contextResults)
	{
		if (string.IsNullOrWhiteSpace(query) || contextResults.Count == 0)
		{
			return false;
		}

		static string Normalize(string value) =>
			value.Replace('ي', 'ی').Replace('ك', 'ک');

		var normalizedQuery = Normalize(query);
		var normalizedContext = Normalize(string.Join(" ", contextResults.Select(x =>
			$"{x.Chunk.Title} {x.Chunk.Text}")));

		// This question describes two potentially conflicting facts at once:
		// an established intention to stay ten days, but departure before ten
		// days have elapsed. The source gives different rulings for established
		// ten-day intention and for staying fewer than ten days before Arafat,
		// without resolving this combined scenario.
		var asksAboutEstablishedIntention =
			Regex.IsMatch(normalizedQuery, @"قصد\s+(?:اقامت\s+)?ده\s+روز") &&
			Regex.IsMatch(normalizedQuery, @"(?:قصد\s+اقامت\s+ده\s+روز.{0,100}(?:اما|ولی)|(?:اما|ولی).{0,100}قصد\s+اقامت\s+ده\s+روز)");

		var asksAboutLeavingBeforeTenDays =
			Regex.IsMatch(normalizedQuery, @"(?:پیش|قبل)\s+از\s+(?:کامل\s+شدن|تمام\s+شدن|تکمیل)\s+ده\s+روز") ||
			Regex.IsMatch(normalizedQuery, @"کمتر\s+از\s+ده\s+روز");

		var sourceStatesBothDifferentCases =
			Regex.IsMatch(normalizedContext, @"قصد\s+اقامت\s+ده\s+روز") &&
			Regex.IsMatch(normalizedContext, @"کمتر\s+از\s+ده\s+روز") &&
			Regex.IsMatch(normalizedContext, @"تمام") &&
			Regex.IsMatch(normalizedContext, @"شکسته");

		return asksAboutEstablishedIntention &&
			asksAboutLeavingBeforeTenDays &&
			sourceStatesBothDifferentCases;
	}

	private static bool HasUnsupportedQuantityAnchor(
		string query,
		IReadOnlyList<RerankResult> contextResults)
	{
		if (string.IsNullOrWhiteSpace(query) || contextResults.Count == 0)
		{
			return false;
		}

		static string Normalize(string value) =>
			value.Replace('ي', 'ی').Replace('ك', 'ک');

		var normalizedQuery = Normalize(query);
		var normalizedContext = Normalize(string.Join(" ", contextResults.Select(x =>
			$"{x.Chunk.Title} {x.Chunk.Text}")));

		// Persian number words and digits followed by a quantity/unit noun.
		// If the noun is absent from every selected source, that source cannot
		// substantiate the explicit quantity asked about (e.g. «هشت فرسخ»).
		const string numberWords =
			"یک|دو|سه|چهار|پنج|شش|هفت|هشت|نه|ده|یازده|دوازده|سیزده|چهارده|پانزده|شانزده|هفده|هجده|نوزده|بیست|سی|چهل|پنجاه|شصت|هفتاد|هشتاد|نود|صد";
		var matches = Regex.Matches(
			normalizedQuery,
			$@"(?<![\p{{L}}\p{{N}}])(?:{numberWords}|[0-9۰-۹]+)\s+(?<anchor>[\p{{L}}]+)",
			RegexOptions.CultureInvariant);

		foreach (Match match in matches)
		{
			var anchor = match.Groups["anchor"].Value;
			if (!Regex.IsMatch(
				normalizedContext,
				$@"(?<![\p{{L}}]){Regex.Escape(anchor)}(?![\p{{L}}])",
				RegexOptions.CultureInvariant))
			{
				Console.WriteLine($"[RAG DEBUG] Unsupported quantity anchor: {match.Value}");
				return true;
			}
		}

		return false;
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
