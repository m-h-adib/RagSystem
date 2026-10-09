using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Rag.Application.Abstractions.Reranking;
using Rag.Domain.Entities;
using Rag.Infrastructure.Ollama;
using Rag.Infrastructure.Rag;

namespace Rag.Tests;

public sealed class RagAnswerServiceRegressionTests
{
    private const string NoAnswer =
        "اطلاعات کافی برای پاسخ به این سؤال در منابع موجود نیست.";

    private const string StaySource =
        "کسانی که در مکه مکرمه قصد اقامت ده روز آنها منعقد شده، در مشاعر نیز نماز آنها تمام است.";

    [Fact]
    public async Task NoRetrievedSources_ReturnsNoAnswerWithoutCallingOllama()
    {
        var handler = new QueueHttpMessageHandler();
        var result = await CreateService(handler).GenerateAnswerAsync("سؤال آزمایشی", []);

        Assert.Equal(NoAnswer, result.Answer);
        Assert.Empty(result.SourceNumbers);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task InvalidRelevancePayload_FailsClosedWithoutGeneratingAnAnswer()
    {
        var handler = new QueueHttpMessageHandler(
            OllamaContent(new { relevantSourceNumbers = "1" }));

        var result = await CreateService(handler).GenerateAnswerAsync(
            "سؤال آزمایشی", Results("متن منبع"));

        Assert.Equal(NoAnswer, result.Answer);
        Assert.Empty(result.SourceNumbers);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task InvalidSourceNumbers_AbstainsWithoutCallingValidator()
    {
        var handler = new QueueHttpMessageHandler(
            OllamaContent(Relevance(1)),
            OllamaContent(JsonSerializer.Serialize(new
            {
                answer = "پاسخ پیشنهادی",
                sourceNumbers = new[] { 3, 3, 3, 3 }
            }));

        var result = await CreateService(handler).GenerateAnswerAsync(
            "سؤال درباره حکم مشخص", Results("منبع مرتبط"));

        Assert.Equal(NoAnswer, result.Answer);
        Assert.Empty(result.SourceNumbers);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task SupportedAnswer_IsReturnedExactlyAsValidated()
    {
        const string answer = "طبق متن، نماز این گروه در مشاعر تمام است.";
        var handler = new QueueHttpMessageHandler(
            OllamaContent(Relevance(1)),
            OllamaContent(JsonSerializer.Serialize(new
            {
                answer,
                sourceNumbers = new[] { 1 }
            })),
            OllamaContent(Validation(true, "نماز این گروه در مشاعر تمام است", StaySource, 1)));

        var result = await CreateService(handler).GenerateAnswerAsync(
            "حکم نماز این گروه در مشاعر چیست؟", Results(StaySource));

        Assert.Equal(answer, result.Answer);
        Assert.Equal(new[] { 1 }, result.SourceNumbers);
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task IrrelevantSourceIsExcluded_AndOriginalCitationNumberIsPreserved()
    {
        const string irrelevant = "اگر خون سه روز دیده شود، حکم روزهای پاکی بین دو خون بررسی می‌شود.";
        var handler = new QueueHttpMessageHandler(
            OllamaContent(Relevance(2)),
            OllamaContent(JsonSerializer.Serialize(new
            {
                answer = "طبق متن، نماز این گروه در مشاعر تمام است.",
                sourceNumbers = new[] { 2 }
            })),
            OllamaContent(Validation(true, "نماز این گروه در مشاعر تمام است", StaySource, 2)));

        var service = CreateService(handler);
        var result = await service.GenerateAnswerAsync(
            "حکم نماز این گروه در مشاعر چیست؟", Results(irrelevant, StaySource));

        Assert.Equal("طبق متن، نماز این گروه در مشاعر تمام است.", result.Answer);
        Assert.Equal(new[] { 2 }, result.SourceNumbers);
        Assert.Contains("[منبع 2]", handler.RequestBodies[1]);
        Assert.DoesNotContain(irrelevant, handler.RequestBodies[1]);
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task UnsupportedAnswer_ReturnsNoAnswerAndNoSources()
    {
        var source = "این منبع فقط درباره موضوعی نزدیک توضیح می‌دهد و حکم مورد سؤال را مشخص نمی‌کند.";
        var handler = new QueueHttpMessageHandler(
            OllamaContent(Relevance(1)),
            OllamaContent(JsonSerializer.Serialize(new
            {
                answer = "نماز در این وضعیت شکسته است.",
                sourceNumbers = new[] { 1 }
            })),
            OllamaContent(Validation(false, "نماز در این وضعیت شکسته است", source, 1)));

        var result = await CreateService(handler).GenerateAnswerAsync(
            "حکم وضعیتی که منبع روشن نکرده چیست؟", Results(source));

        Assert.Equal(NoAnswer, result.Answer);
        Assert.Empty(result.SourceNumbers);
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task DuplicateClaimsInValidation_FailClosed()
    {
        var handler = new QueueHttpMessageHandler(
            OllamaContent(Relevance(1)),
            OllamaContent(JsonSerializer.Serialize(new
            {
                answer = "طبق متن، نماز این گروه در مشاعر تمام است.",
                sourceNumbers = new[] { 1 }
            })),
            OllamaContent(JsonSerializer.Serialize(new
            {
                supported = true,
                reason = "تکرار ادعا",
                claimChecks = new[]
                {
                    new { claim = "نماز این گروه در مشاعر تمام است", supported = true, evidence = StaySource, sourceNumber = 1 },
                    new { claim = "نماز این گروه در مشاعر تمام است", supported = true, evidence = StaySource, sourceNumber = 1 }
                }
            })));

        var result = await CreateService(handler).GenerateAnswerAsync(
            "حکم نماز این گروه در مشاعر چیست؟", Results(StaySource));

        Assert.Equal(NoAnswer, result.Answer);
        Assert.Empty(result.SourceNumbers);
    }

    [Fact]
    public async Task InvalidSemanticValidationResponse_FailsClosed()
    {
        var handler = new QueueHttpMessageHandler(
            OllamaContent(Relevance(1)),
            OllamaContent(JsonSerializer.Serialize(new
            {
                answer = "پاسخ پیشنهادی",
                sourceNumbers = new[] { 1 }
            }),
            OllamaContent("not-json"));

        var result = await CreateService(handler).GenerateAnswerAsync(
            "سؤال آزمایشی", Results("متن منبع"));

        Assert.Equal(NoAnswer, result.Answer);
        Assert.Empty(result.SourceNumbers);
        Assert.Equal(3, handler.CallCount);
    }

    private static RagAnswerService CreateService(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("http://ollama.test") },
            Options.Create(new OllamaOptions { Model = "test-model" }),
            Options.Create(new RagOptions { ContextCount = 3 }));

    private static IReadOnlyList<RerankResult> Results(params string[] texts) =>
        texts.Select((text, index) => new RerankResult(
            new Chunk
            {
                Id = $"chunk-{index + 1}",
                DocumentId = $"document-{index + 1}",
                Title = $"عنوان آزمایشی {index + 1}",
                Text = text,
                Index = index
            },
            1.0f - index * 0.1f)).ToList();

    private static object Relevance(params int[] numbers) =>
        new { relevantSourceNumbers = numbers };

    private static object Validation(bool supported, string claim, string evidence, int sourceNumber) =>
        new
        {
            supported,
            reason = supported ? "پاسخ پشتیبانی می‌شود." : "پاسخ پشتیبانی نمی‌شود.",
            claimChecks = new[]
            {
                new { claim, supported, evidence, sourceNumber }
            }
        };

    private static string OllamaContent(object content) =>
        JsonSerializer.Serialize(new { message = new { content = JsonSerializer.Serialize(content) } });

    private sealed class QueueHttpMessageHandler(params string[] responses) : HttpMessageHandler
    {
        private readonly Queue<string> _responses = new(responses);
        public int CallCount { get; private set; }
        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            RequestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));

            if (_responses.Count == 0)
                throw new InvalidOperationException("Unexpected HTTP request in test.");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    _responses.Dequeue(), Encoding.UTF8, "application/json")
            };
        }
    }
}
