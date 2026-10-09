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

    private const string NoAnswerText =
        "اطلاعات کافی برای پاسخ به این سؤال در منابع موجود نیست.";

    public async Task<RagAnswerResult> GenerateAnswerAsync(
        string query,
        IReadOnlyList<RerankResult> results,
        CancellationToken cancellationToken = default)
    {
        if (results.Count == 0)
            return new RagAnswerResult(NoAnswerText, []);

        var contextResults = results
            .Take(Math.Clamp(_ragOptions.ContextCount, 1, 20))
            .ToList();

        var context = string.Join(
            "\n\n",
            contextResults.Select((result, index) => $"""
                [منبع {index + 1}]
                عنوان: {result.Chunk.Title ?? ""}
                متن:
                {result.Chunk.Text}
                """));

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
            - فقط بر پایه متن منابع بالا پاسخ بده؛ از دانش بیرونی استفاده نکن.
            - ابتدا تمام خواسته‌ها، شرط‌ها، استثناها و مقایسه‌های سؤال را تشخیص بده.
            - فقط وقتی پاسخ بده که منابع برای همه بخش‌های ضروری سؤال شواهد مستقیم و کافی دارند.
            - شباهت موضوعی یا واژگانی به‌تنهایی دلیل کافی برای پاسخ نیست.
            - هیچ نام، حکم، عدد، شرط، استثنا یا رابطه‌ای را از یک بند یا گروه به بند دیگر منتقل نکن.
            - اگر منبع چند بند یا گروه متمایز دارد، نسبت هر ادعا به همان بند را حفظ کن.
            - اگر شواهد کافی نیست یا پاسخ مستلزم حدس است، answer را دقیقاً برابر «{NoAnswerText}» قرار بده و sourceNumbers را آرایه خالی برگردان.
            - sourceNumbers فقط شماره برچسب‌های «[منبع N]» در ابتدای منابع بازیابی‌شده است؛ هرگز شماره بندها، گزینه‌ها، مسائل یا فهرست‌های داخل متن یک منبع را در sourceNumbers قرار نده.
            - هر منبع با برچسب مستقل «[منبع N]» مشخص شده است. اگر پاسخ فقط از منبعی با برچسب «[منبع 1]» استفاده می‌کند، sourceNumbers باید [1] باشد، حتی اگر متن همان منبع شامل بندهای شماره‌دار 1، 2، 3 و ... باشد.
            - sourceNumbers فقط شامل شماره منابعی باشد که مستقیماً برای پاسخ استفاده شده‌اند؛ شماره‌ها باید از 1 تا {contextResults.Count} باشند.
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
            .Where(number => number >= 1 && number <= contextResults.Count)
            .Distinct()
            .OrderBy(number => number)
            .ToList();

        if (sourceNumbers.Count == 0)
        {
            Console.WriteLine("[RAG DEBUG] No valid source numbers returned; abstaining.");
            return new RagAnswerResult(NoAnswerText, []);
        }

        var selectedSources = string.Join(
            "\n\n",
            sourceNumbers.Select(number =>
            {
                var chunk = contextResults[number - 1].Chunk;
                return $"[منبع {number}]\nعنوان: {chunk.Title ?? ""}\nمتن: {chunk.Text}";
            }));

        if (!await IsAnswerSupportedAsync(
                query, answer, selectedSources, cancellationToken))
        {
            Console.WriteLine("[RAG DEBUG] Semantic support validation failed; abstaining.");
            return new RagAnswerResult(NoAnswerText, []);
        }

        // Return exactly the text that was validated. Do not post-process it with
        // a second rule that could change claims after validation.
        return new RagAnswerResult(answer, sourceNumbers);
    }

    private async Task<bool> IsAnswerSupportedAsync(
        string query,
        string answer,
        string selectedSources,
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
            {selectedSources}

            پاسخ را به ادعاهای مستقل و قابل بررسی تقسیم کن؛ هر حکم، شرط، استثنا، عدد و نسبت دادن یک حکم به یک گروه، ادعای جداگانه است.
            برای هر ادعا، یک شاهد کوتاه را عیناً از متن یکی از منابع نقل کن.
            شاهد باید دقیقاً در متن منابع وجود داشته باشد؛ نقل‌قولی که بازنویسی شده یا فقط موضوع مشابهی دارد معتبر نیست.
            صرف وجود واژه‌های مشابه، ارتباط موضوعی، یا کنار هم قرار گرفتن دو واقعیت، پشتیبانی مستقیم از ادعا محسوب نمی‌شود.
            اگر حتی یک ادعای مهم شاهد مستقیم ندارد، یا شاهد نقل‌شده ادعا را نتیجه نمی‌دهد، آن ادعا را supported=false علامت بزن.
            در کل supported فقط وقتی true باشد که فهرست ادعاها خالی نباشد، تمام ادعاها supported=true باشند و برای هر ادعا شاهد دقیق وجود داشته باشد.
            فیلد evidence باید نقل‌قول عین متن منبع باشد؛ اگر شاهدی نیست، رشته خالی باشد.
            فقط JSON برگردان که شامل فیلد supported از نوع boolean، فیلد reason از نوع string و فیلد claimChecks از نوع آرایه باشد. هر عضو claimChecks باید سه فیلد داشته باشد: claim از نوع string، supported از نوع boolean و evidence از نوع string.
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
                                evidence = new { type = "string" }
                            },
                            required = new[] { "claim", "supported", "evidence" }
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
                return false;

            var validation = JsonSerializer.Deserialize<SupportCheckResponse>(
                raw,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var claimChecks = validation?.ClaimChecks ?? [];
            var evidenceIsGrounded = claimChecks.Count > 0 &&
                claimChecks.All(check =>
                    check.Supported &&
                    !string.IsNullOrWhiteSpace(check.Claim) &&
                    !string.IsNullOrWhiteSpace(check.Evidence) &&
                    selectedSources.Contains(check.Evidence, StringComparison.Ordinal));

            Console.WriteLine(
                $"[RAG DEBUG] Semantic support validation: {validation?.Supported}; " +
                $"claim checks: {claimChecks.Count}; grounded evidence: {evidenceIsGrounded}; " +
                $"reason: {validation?.Reason}");

            foreach (var check in claimChecks)
            {
                Console.WriteLine(
                    $"[RAG DEBUG] Claim supported: {check.Supported}; " +
                    $"evidence found verbatim: {!string.IsNullOrWhiteSpace(check.Evidence) && selectedSources.Contains(check.Evidence, StringComparison.Ordinal)}; " +
                    $"claim: {check.Claim}");
            }

            // Fail closed unless every checked claim has a verbatim evidence quote
            // present in the selected source text.
            return validation?.Supported == true && evidenceIsGrounded;
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
            return false;
        }
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
