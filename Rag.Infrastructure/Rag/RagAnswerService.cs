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

    private const string NoAnswerText =
        "اطلاعات کافی برای پاسخ به این سؤال در منابع موجود نیست.";

    public async Task<RagAnswerResult> GenerateAnswerAsync(
        string query,
        IReadOnlyList<RerankResult> results,
        CancellationToken cancellationToken = default)
    {
        if (results.Count == 0)
            return new RagAnswerResult(NoAnswerText, []);

        var candidateResults = results
            .Take(Math.Clamp(_ragOptions.ContextCount, 1, 20))
            .ToList();

        // Preserve original candidate numbering because SearchController maps returned
        // source numbers against the ordered candidate list passed into this service.
        var selectedSourceNumbers = await GetRelevantSourceNumbersAsync(
            query, candidateResults, cancellationToken);

        if (selectedSourceNumbers is null || selectedSourceNumbers.Count == 0)
        {
            Console.WriteLine("[RAG DEBUG] No confidently relevant sources; abstaining.");
            return new RagAnswerResult(NoAnswerText, []);
        }

        var context = string.Join(
            "\n\n",
            selectedSourceNumbers.Select(number =>
            {
                var result = candidateResults[number - 1];
                return $"""
                    [منبع {number}]
                    عنوان: {result.Chunk.Title ?? ""}
                    متن:
                    {result.Chunk.Text}
                    """;
            }));

        Console.WriteLine("[RAG DEBUG] Context passed to Ollama:");
        Console.WriteLine(context);
        Console.WriteLine("[RAG DEBUG] End of Ollama context.");

        var prompt = $"""
            نقش: دستیار پرسش‌وپاسخ مبتنی بر اسناد.

            سؤال:
            {query}

            منابع بازیابی‌شده:
            {context}

            دستورالعمل:
            - فقط بر پایه متن منابع بالا پاسخ بده؛ از دانش بیرونی، حدس یا دانسته‌های عمومی استفاده نکن.
            - ابتدا تمام خواسته‌ها، شرط‌ها، استثناها و مقایسه‌های سؤال را تشخیص بده.
            - فقط از منابعی استفاده کن که در مرحله تشخیص ارتباط، برای همین سؤال انتخاب شده‌اند؛ منبعی با موضوع یا شرط متفاوت را صرفاً به‌دلیل داشتن واژه‌های مشابه به کار نبر.
            - پیش از نوشتن هر جمله، مطمئن شو تمام اجزای آن مستقیماً در متن یک منبع یا با ترکیب مجاز چند منبع مرتبط پشتیبانی می‌شوند؛ اگر حتی یک جزء شاهد ندارد، آن جمله را حذف کن.
            - هیچ حکم، نتیجه، عدد، شرط، استثنا، اثر شرعی یا رابطه‌ای را که صریحاً در منبع نیامده، استنتاج یا اضافه نکن.
            - بندهای یک منبع را دقیق بخوان؛ شماره‌گذاری، مثال‌ها و احکام مربوط به گروه‌ها یا شرایط مختلف را با هم ادغام نکن.
            - از منابع نامرتبط برای پر کردن خلأ پاسخ استفاده نکن. اگر منابع مرتبط برای پاسخ کافی نیستند، پاسخ دقیقاً برابر «{NoAnswerText}» و sourceNumbers آرایه خالی باشد.
            - فقط وقتی پاسخ بده که منابع مرتبط برای همه بخش‌های ضروری سؤال شواهد مستقیم و کافی دارند.
            - شباهت موضوعی یا واژگانی به‌تنهایی دلیل کافی برای پاسخ نیست.
            - هیچ نام، حکم، عدد، شرط، استثنا یا رابطه‌ای را از یک بند یا گروه به بند دیگر منتقل نکن.
            - اگر منبع چند بند یا گروه متمایز دارد، نسبت هر ادعا به همان بند را حفظ کن.
            - اگر شواهد کافی نیست یا پاسخ مستلزم حدس است، answer را دقیقاً برابر «{NoAnswerText}» قرار بده و sourceNumbers را آرایه خالی برگردان.
            - sourceNumbers فقط شماره برچسب‌های «[منبع N]» در ابتدای منابع بازیابی‌شده است؛ هرگز شماره بندها، گزینه‌ها، مسائل یا فهرست‌های داخل متن یک منبع را در sourceNumbers قرار نده.
            - هر منبع با برچسب مستقل «[منبع N]» مشخص شده است. اگر پاسخ فقط از منبعی با برچسب «[منبع 1]» استفاده می‌کند، sourceNumbers باید [1] باشد، حتی اگر متن همان منبع شامل بندهای شماره‌دار 1، 2، 3 و ... باشد.
            - sourceNumbers فقط شامل شماره منابعی باشد که مستقیماً برای پاسخ استفاده شده‌اند؛ شماره‌ها باید از 1 تا {candidateResults.Count} باشند.
            - پاسخ را روشن، مستقل و به زبان سؤال بنویس؛ اطلاعات مرتبط و ضروری را حذف نکن.
            - خروجی فقط JSON مطابق ساختار تعیین‌شده باشد.

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
                    answer = new { type = "string" },
                    sourceNumbers = new
                    {
                        type = "array",
                        items = new { type = "integer" }
                    }
                },
                required = new[] { "answer", "sourceNumbers" }
            },
            messages = new[]
            {
                new { role = "user", content = prompt }
            }
        };

        using var response = await httpClient.PostAsJsonAsync(
            "/api/chat",
            request,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var ollamaResponse = await response.Content.ReadFromJsonAsync<OllamaResponse>(
            cancellationToken);

        var rawContent = ollamaResponse?.Message.Content;
        if (string.IsNullOrWhiteSpace(rawContent))
            return new RagAnswerResult(NoAnswerText, []);

        Console.WriteLine("===== OLLAMA RAW RAG RESPONSE =====");
        Console.WriteLine(rawContent);
        Console.WriteLine("===================================");

        RagLlmResponse? result;
        try
        {
            result = JsonSerializer.Deserialize<RagLlmResponse>(
                rawContent,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return new RagAnswerResult(NoAnswerText, []);
        }

        if (result is null || string.IsNullOrWhiteSpace(result.Answer))
            return new RagAnswerResult(NoAnswerText, []);

        var answer = result.Answer.Trim();
        if (answer == NoAnswerText)
            return new RagAnswerResult(NoAnswerText, []);

        // Citations are untrusted model output. Reject invalid indices rather than
        // silently mapping them to another source or returning an uncited answer.
        var sourceNumbers = (result.SourceNumbers ?? [])
            .Where(number => number >= 1 && number <= candidateResults.Count)
            .Distinct()
            .OrderBy(number => number)
            .ToList();

        if (sourceNumbers.Count == 0)
        {
            Console.WriteLine("[RAG DEBUG] No valid source numbers returned; abstaining.");
            return new RagAnswerResult(NoAnswerText, []);
        }

        // Validate against every retrieved context chunk. The answer model can
        // accidentally omit a source number even when its answer uses that source.
        // The validator identifies the source for each evidence quote; only those
        // sources are returned as citations after exact quote verification.
        var validatedSourceNumbers = await IsAnswerSupportedAsync(
            query, answer, context, candidateResults, selectedSourceNumbers, cancellationToken);

        if (validatedSourceNumbers is null || validatedSourceNumbers.Count == 0)
        {
            Console.WriteLine("[RAG DEBUG] Semantic support validation failed; abstaining.");
            return new RagAnswerResult(NoAnswerText, []);
        }

        // Return exactly the text that was validated and cite only sources whose
        // body contains evidence accepted by the validator.
        return new RagAnswerResult(answer, validatedSourceNumbers);
    }

    private async Task<List<int>?> GetRelevantSourceNumbersAsync(
        string query,
        IReadOnlyList<RerankResult> candidateResults,
        CancellationToken cancellationToken)
    {
        var sourceContext = string.Join(
            "\n\n",
            candidateResults.Select((result, index) => $"""
                [منبع {index + 1}]
                عنوان: {result.Chunk.Title ?? ""}
                متن:
                {result.Chunk.Text}
                """));

        var prompt = $"""
            نقش: ارزیاب ارتباط منبع با سؤال برای یک سامانه RAG.

            سؤال کاربر:
            {query}

            منابع نامزد:
            {sourceContext}

            برای هر منبع تصمیم بگیر آیا محتوای آن مستقیماً برای پاسخ‌دادن به همین سؤال لازم و مرتبط است.
            فقط شماره منابعی را انتخاب کن که همان موضوع، وضعیت، شرط و نوع حکم مورد سؤال را پوشش می‌دهند.
            منبعی را فقط به دلیل داشتن واژه‌های مشابه انتخاب نکن.
            منبعی درباره وضعیت، حکم، گروه اشخاص یا شرط متفاوت را مرتبط محسوب نکن؛
            برای مثال، یک قاعده درباره یک وضعیت متفاوت را صرفاً به خاطر شباهت واژگانی وارد نکن.
            اگر منبع فقط بخشی از موضوع عمومی را ذکر می‌کند اما برای سؤال فعلی کاربرد مستقیم ندارد، آن را حذف کن.
            اگر در ارتباط منبع با سؤال تردید داری، آن را حذف کن.
            اگر هیچ منبعی مستقیماً مرتبط نیست، آرایه خالی برگردان.
            این مرحله صحت حکم را تأیید نمی‌کند؛ فقط ارتباط منبع با سؤال را می‌سنجد.
            شماره‌ها باید دقیقاً با برچسب [منبع N] مطابقت داشته باشند.

            فقط JSON با فیلد relevantSourceNumbers از نوع آرایه اعداد صحیح برگردان.
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
                    relevantSourceNumbers = new
                    {
                        type = "array",
                        items = new { type = "integer" }
                    }
                },
                required = new[] { "relevantSourceNumbers" }
            },
            messages = new[] { new { role = "user", content = prompt } }
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
                return null;

            Console.WriteLine("===== OLLAMA RAW SOURCE RELEVANCE =====");
            Console.WriteLine(raw);
            Console.WriteLine("=======================================");

            using var document = JsonDocument.Parse(raw);
            if (!document.RootElement.TryGetProperty("relevantSourceNumbers", out var numbers) ||
                numbers.ValueKind != JsonValueKind.Array)
                return null;

			var selected = numbers
                .EnumerateArray()
                .Where(item =>
                    item.ValueKind == JsonValueKind.Number &&
                    item.TryGetInt32(out _))
                .Select(item => item.GetInt32())
                .Where(number =>
                    number >= 1 &&
                    number <= candidateResults.Count)
                .Distinct()
                .OrderBy(number => number)
                .ToList();

			Console.WriteLine(
                $"[RAG DEBUG] Relevant source candidates: {string.Join(", ", selected)}");
            return selected;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Fail closed if relevance classification fails; do not fall back to
            // passing potentially unrelated chunks into answer generation.
            Console.WriteLine(
                $"[RAG DEBUG] Source relevance check failed; abstaining. {exception.Message}");
            return null;
        }
    }

    private async Task<List<int>?> IsAnswerSupportedAsync(
        string query,
        string answer,
        string allSources,
        IReadOnlyList<RerankResult> candidateResults,
        IReadOnlyList<int> selectedSourceNumbers,
        CancellationToken cancellationToken)
    {
        var validationPrompt = $"""
            شما اعتبارسنج مستقل یک پاسخ مبتنی بر منبع هستید.
            بررسی کنید آیا تمام ادعاهای پاسخ مستقیماً از منابع ارائه‌شده پشتیبانی می‌شوند
            و آیا پاسخ به همه بخش‌های ضروری سؤال جواب می‌دهد.

            supported=true فقط وقتی مجاز است که:
            - تمام بخش‌های ضروری سؤال پاسخ داده شده باشند؛
            - تمام ادعاها، نام‌ها، اعداد، شرط‌ها و استثناهای پاسخ مستقیماً در منبع باشند؛
            - هیچ دو حکم یا گروه متفاوتی با هم ترکیب نشده باشند؛
            - پاسخ برای حالت یا شرطی که منبع بیان نکرده، نتیجه‌گیری نکرده باشد.

            اگر پاسخ ناقص است، منبع فقط موضوعی مشابه را بیان می‌کند، منبع مبهم است،
            یا هر ادعا به حدس نیاز دارد، supported=false.
            در صورت تردید false برگردان.
            از دانش بیرونی برای داوری استفاده نکن؛ فقط پشتیبانی متنی را بررسی کن.

            سؤال:
            {query}

            پاسخ:
            {answer}

            منابع:
            {allSources}

            پاسخ را به ادعاهای اتمی و کوتاه تقسیم کن؛ هر ادعا فقط یک حکم یا شرط مستقل داشته باشد و هرگز چند حکم را در یک claim جمع نکن.
            هر ادعای پاسخ را دقیقاً یک بار بررسی کن؛ claim تکراری نساز و ادعاهایی را که پاسخ واقعاً نگفته به فهرست اضافه نکن.
            فقط از منابع فهرست‌شده در همین پیام شاهد انتخاب کن و sourceNumber را دقیقاً مطابق شماره اصلی [منبع N] نگه دار.
            برای هر ادعا، فقط یک شاهد کوتاه را عیناً از بدنه یکی از منابع نقل کن.
            evidence باید فقط نقل‌قول متن منبع باشد؛ شماره منبع، عنوان، عبارت «متن:» و سه‌نقطه را داخل آن نیاور.
            شاهد باید عیناً در بدنه یکی از منابع وجود داشته باشد؛ بازنویسی یا شباهت موضوعی معتبر نیست.
            صرف وجود واژه‌های مشابه، ارتباط موضوعی، یا کنار هم قرار گرفتن دو واقعیت، پشتیبانی مستقیم از ادعا محسوب نمی‌شود.
            اگر حتی یک ادعای مهم شاهد مستقیم ندارد، یا شاهد نقل‌شده ادعا را نتیجه نمی‌دهد، آن ادعا را supported=false علامت بزن.
            در کل supported فقط وقتی true باشد که فهرست ادعاها خالی نباشد، تمام ادعاها supported=true باشند و برای هر ادعا شاهد دقیق وجود داشته باشد.
            برای هر شاهد، sourceNumber را شماره همان منبعی قرار بده که بدنه‌اش شامل نقل‌قول است؛ شماره باید با برچسب [منبع N] مطابقت داشته باشد.
            اگر شاهدی نیست، evidence را رشته خالی و sourceNumber را 0 قرار بده.
            فیلد evidence باید نقل‌قول عین متن بدنه همان منبع باشد؛ عنوان و برچسب منبع جزو شاهد نیستند.
            فقط JSON برگردان که شامل فیلدهای supported از نوع boolean، reason از نوع string و claimChecks از نوع آرایه باشد. هر عضو claimChecks باید فیلدهای claim از نوع string، supported از نوع boolean، evidence از نوع string و sourceNumber از نوع integer داشته باشد.
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
                    reason = new { type = "string" },
                    claimChecks = new
                    {
                        type = "array",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                claim = new { type = "string" },
                                supported = new { type = "boolean" },
                                evidence = new { type = "string" },
                                sourceNumber = new { type = "integer" }
                            },
                            required = new[] { "claim", "supported", "evidence", "sourceNumber" }
                        }
                    }
                },
                required = new[] { "supported", "reason", "claimChecks" }
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
                return null;

            Console.WriteLine("===== OLLAMA RAW SUPPORT VALIDATION =====");
            Console.WriteLine(raw);
            Console.WriteLine("=========================================");

            var validation = JsonSerializer.Deserialize<SupportCheckResponse>(
                raw,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var claimChecks = validation?.ClaimChecks ?? [];
            var hasUniqueClaims = claimChecks
                .Select(check => NormalizeEvidenceText(check.Claim))
                .Distinct(StringComparer.Ordinal)
                .Count() == claimChecks.Count;

            var evidenceIsGrounded = claimChecks.Count > 0 &&
                hasUniqueClaims &&
                claimChecks.All(check =>
                    check.Supported &&
                    !string.IsNullOrWhiteSpace(check.Claim) &&
                    check.SourceNumber >= 1 &&
                    check.SourceNumber <= candidateResults.Count &&
                    selectedSourceNumbers.Contains(check.SourceNumber) &&
                    IsVerbatimEvidence(
                        check.Evidence,
                        candidateResults[check.SourceNumber - 1].Chunk.Text ?? string.Empty) &&
                    HasSufficientClaimEvidence(check.Claim, check.Evidence));

            Console.WriteLine(
                $"[RAG DEBUG] Semantic support validation: {validation?.Supported}; " +
                $"claim checks: {claimChecks.Count}; grounded evidence: {evidenceIsGrounded}; " +
                $"reason: {validation?.Reason}");

            var evidenceSourceNumbers = new HashSet<int>();
            foreach (var check in claimChecks)
            {
                var evidenceFoundVerbatim =
                    check.SourceNumber >= 1 &&
                    check.SourceNumber <= candidateResults.Count &&
                    selectedSourceNumbers.Contains(check.SourceNumber) &&
                    IsVerbatimEvidence(
                        check.Evidence,
                        candidateResults[check.SourceNumber - 1].Chunk.Text ?? string.Empty);
                var evidenceMatchesClaim = evidenceFoundVerbatim &&
                    HasSufficientClaimEvidence(check.Claim, check.Evidence);

                if (check.Supported && evidenceMatchesClaim)
                    evidenceSourceNumbers.Add(check.SourceNumber);

                Console.WriteLine(
                    $"[RAG DEBUG] Claim supported: {check.Supported}; " +
                    $"evidence found verbatim: {evidenceFoundVerbatim}; " +
                    $"evidence matches claim: {evidenceMatchesClaim}; " +
                    $"source: {check.SourceNumber}; claim: {check.Claim}");
                Console.WriteLine($"[RAG DEBUG] Evidence returned: >>>{check.Evidence}<<<");
            }

            // Fail closed unless every checked claim has a verbatim evidence quote
            // present in the cited source body.
            return validation?.Supported == true && evidenceIsGrounded
                ? evidenceSourceNumbers.OrderBy(number => number).ToList()
                : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Fail closed: never return an answer if the validation stage failed.
            Console.WriteLine(
                $"[RAG DEBUG] Semantic validator failed; abstaining. {exception.Message}");
            return null;
        }
    }

    private static bool IsVerbatimEvidence(string? evidence, string sourceText)
    {
        if (string.IsNullOrWhiteSpace(evidence) ||
            evidence.Contains("...", StringComparison.Ordinal) ||
            evidence.Contains("…", StringComparison.Ordinal))
            return false;

        var normalizedEvidence = NormalizeEvidenceText(evidence);
        if (normalizedEvidence.Length < 12)
            return false;

        var normalizedSource = NormalizeEvidenceText(sourceText);
        if (normalizedSource.Contains(normalizedEvidence, StringComparison.Ordinal))
            return true;

        // Ollama occasionally makes a one-character typo while copying a quote.
        // Permit at most one single-character typo in one token, while requiring
        // all other tokens to match in the same order and contiguously. This does
        // not accept paraphrases or unrelated evidence.
        var evidenceTokens = Regex.Matches(normalizedEvidence, @"[\p{L}\p{Nd}]+")
            .Select(match => match.Value)
            .ToArray();
        var sourceTokens = Regex.Matches(normalizedSource, @"[\p{L}\p{Nd}]+")
            .Select(match => match.Value)
            .ToArray();

        if (evidenceTokens.Length < 3 || sourceTokens.Length < evidenceTokens.Length)
            return false;

        for (var start = 0; start <= sourceTokens.Length - evidenceTokens.Length; start++)
        {
            var typoCount = 0;
            var matches = true;

            for (var offset = 0; offset < evidenceTokens.Length; offset++)
            {
                var expected = evidenceTokens[offset];
                var actual = sourceTokens[start + offset];

                if (string.Equals(expected, actual, StringComparison.Ordinal))
                    continue;

                if (typoCount > 0 || expected.Length < 5 || actual.Length < 5 ||
                    !DiffersByOneCharacter(expected, actual))
                {
                    matches = false;
                    break;
                }

                typoCount++;
            }

            if (matches && typoCount == 1)
                return true;
        }

        return false;
    }

    private static bool DiffersByOneCharacter(string left, string right)
    {
        if (Math.Abs(left.Length - right.Length) > 1)
            return false;

        var leftIndex = 0;
        var rightIndex = 0;
        var differences = 0;

        while (leftIndex < left.Length && rightIndex < right.Length)
        {
            if (left[leftIndex] == right[rightIndex])
            {
                leftIndex++;
                rightIndex++;
                continue;
            }

            if (++differences > 1)
                return false;

            if (left.Length > right.Length)
                leftIndex++;
            else if (right.Length > left.Length)
                rightIndex++;
            else
            {
                leftIndex++;
                rightIndex++;
            }
        }

        if (leftIndex < left.Length || rightIndex < right.Length)
            differences++;

        return differences == 1;
    }

    private static bool HasSufficientClaimEvidence(string? claim, string? evidence)
    {
        if (string.IsNullOrWhiteSpace(claim) || string.IsNullOrWhiteSpace(evidence))
            return false;

        // A verbatim quote alone is not proof that it supports the claim. Require
        // substantial lexical coverage of the claim by the quote from that same
        // source. This is a conservative guard against a validator attaching an
        // unrelated quote (for example, a wudu rule) to a claim about a ten-day
        // menstrual condition. Semantic validation remains necessary; this check
        // is an additional deterministic rejection rule, not a proof of entailment.
        var stopWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "اگر", "برای", "براي", "این", "آن", "یک", "است", "باشد", "شود",
            "شده", "می", "نیز", "یا", "و", "در", "از", "به", "با", "را",
            "که", "تا", "هر", "هم", "پس", "بعد", "قبل", "روی", "روی", "زمان",
            "خود", "همان", "باید", "لازم", "نسبت", "مربوط", "دارد", "دارند"
        };

        static HashSet<string> GetContentTerms(string value, HashSet<string> stopWords)
        {
            var normalized = NormalizeEvidenceText(value)
                .Replace('،', ' ')
                .Replace('؛', ' ')
                .Replace('؟', ' ')
                .Replace('.', ' ')
                .Replace(':', ' ')
                .Replace(';', ' ');

            return Regex.Matches(normalized, @"[\p{L}\p{Nd}]+")
                .Select(match => match.Value)
                .Where(token => token.Length >= 3 && !stopWords.Contains(token))
                .ToHashSet(StringComparer.Ordinal);
        }

        var claimTerms = GetContentTerms(claim, stopWords);
        var evidenceTerms = GetContentTerms(evidence, stopWords);

        if (claimTerms.Count < 3 || evidenceTerms.Count < 2)
            return false;

        var matchedTerms = claimTerms.Count(evidenceTerms.Contains);
        var coverage = (double)matchedTerms / claimTerms.Count;

        return matchedTerms >= 2 && coverage >= 0.55;
    }

    private static string NormalizeEvidenceText(string value)
    {
        var normalized = value
            .Replace('ي', 'ی')
            .Replace('ى', 'ی')
            .Replace('ك', 'ک')
            .Replace('\u0640', ' ')
            .Replace('\u200c', ' ')
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ');

        return string.Join(' ', normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed class SupportCheckResponse
    {
        public bool Supported { get; set; }
        public string Reason { get; set; } = string.Empty;
        public List<ClaimCheck> ClaimChecks { get; set; } = [];
    }

    private sealed class ClaimCheck
    {
        public string Claim { get; set; } = string.Empty;
        public bool Supported { get; set; }
        public string Evidence { get; set; } = string.Empty;
        public int SourceNumber { get; set; }
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
