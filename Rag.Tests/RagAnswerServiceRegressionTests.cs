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
        "کسانی که در مکه مکرمه قصد اقامت ده روز آنها منعقد شده، در مشاعر نیز نماز آنها تمام است و کسانی که در مکه مکرمه قبل از رفتن به عرفات کمتر از ده روز اقامت داشته‌اند، نمازشان در مشاعر شکسته است.";

    [Fact]
    public async Task NoRetrievedSources_ReturnsNoAnswerWithoutCallingOllama()
    {
        var handler = new QueueHttpMessageHandler();
        var service = CreateService(handler);

        var result = await service.GenerateAnswerAsync("سؤال آزمایشی", []);

        Assert.Equal(NoAnswer, result.Answer);
        Assert.Empty(result.SourceNumbers);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task QuantityMissingFromSources_AbstainsBeforeCallingOllama()
    {
        var handler = new QueueHttpMessageHandler();
        var service = CreateService(handler);
        var sources = Results(StaySource);

        var result = await service.GenerateAnswerAsync(
            "شرایط هشت فرسخ برای شکسته شدن نماز چیست؟",
            sources);

        Assert.Equal(NoAnswer, result.Answer);
        Assert.Empty(result.SourceNumbers);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task MenstruationQuestion_WithOnlyIstihadaSource_AbstainsBeforeCallingOllama()
    {
        var handler = new QueueHttpMessageHandler();
        var service = CreateService(handler);
        const string query = "زنی که در دوران قاعدگی هست میتونه طواف انجام بده یا خیر؟";
        const string istihadaSource = "مسئله7: ورود مستحاضه متوسطه و كثيره به مسجدين؛ جايز است، اگرچه غسل‌های واجبش را انجام نداده باشد.";

        var result = await service.GenerateAnswerAsync(query, Results(istihadaSource));

        Assert.Equal(NoAnswer, result.Answer);
        Assert.Empty(result.SourceNumbers);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task CombinedStayDurationConditions_AbstainsBeforeCallingOllama()
    {
        var handler = new QueueHttpMessageHandler();
        var service = CreateService(handler);
        var query = "اگر کسی در مکه قصد اقامت ده روز داشته باشد، اما پیش از کامل شدن ده روز به عرفات برود، آیا نمازش در عرفات تمام است یا شکسته؟";

        var result = await service.GenerateAnswerAsync(query, Results(StaySource));

        Assert.Equal(NoAnswer, result.Answer);
        Assert.Empty(result.SourceNumbers);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task DirectlySupportedAnswer_WithPositiveValidation_ReturnsAnswerAndSource()
    {
        const string answer =
            "کسانی که قصد اقامت ده روزشان در مکه منعقد شده است، در مشاعر نماز را تمام می‌خوانند.";

        var handler = new QueueHttpMessageHandler(
            OllamaContent(JsonSerializer.Serialize(new
            {
                answer,
                sourceNumbers = new[] { 1 }
            })),
            OllamaContent(JsonSerializer.Serialize(new
            {
                supported = true,
                reason = "پاسخ و شرط آن مستقیماً در منبع آمده است."
            })));

        var service = CreateService(handler);
        var result = await service.GenerateAnswerAsync(
            "کسانی که در مکه قصد اقامت ده روز دارند، در عرفات و منا نماز را تمام می‌خوانند یا شکسته؟",
            Results(StaySource));

        Assert.Equal(answer, result.Answer);
        Assert.Equal(new[] { 1 }, result.SourceNumbers);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task NegativeSemanticValidation_ReturnsNoAnswerAndNoSources()
    {
        var handler = new QueueHttpMessageHandler(
            OllamaContent(JsonSerializer.Serialize(new
            {
                answer = "نماز در این وضعیت تمام است.",
                sourceNumbers = new[] { 1 }
            })),
            OllamaContent(JsonSerializer.Serialize(new
            {
                supported = false,
                reason = "منبع این وضعیت دقیق را روشن نمی‌کند."
            })));

        var service = CreateService(handler);
        var result = await service.GenerateAnswerAsync(
            "سؤال درباره حکم مشخصی که منبع روشن نکرده است",
            Results("منبع درباره موضوعی نزدیک توضیح می‌دهد، اما این وضعیت را مشخص نمی‌کند."));

        Assert.Equal(NoAnswer, result.Answer);
        Assert.Empty(result.SourceNumbers);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task InvalidSemanticValidationResponse_FailsClosed()
    {
        var handler = new QueueHttpMessageHandler(
            OllamaContent(JsonSerializer.Serialize(new
            {
                answer = "پاسخ پیشنهادی",
                sourceNumbers = new[] { 1 }
            })),
            OllamaContent("not-json"));

        var service = CreateService(handler);
        var result = await service.GenerateAnswerAsync(
            "سؤال آزمایشی",
            Results("متن منبع"));

        Assert.Equal(NoAnswer, result.Answer);
        Assert.Empty(result.SourceNumbers);
        Assert.Equal(2, handler.CallCount);
    }

    private static RagAnswerService CreateService(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("http://ollama.test") },
            Options.Create(new OllamaOptions { Model = "test-model" }),
            Options.Create(new RagOptions { ContextCount = 3 }));

    private static IReadOnlyList<RerankResult> Results(string text) =>
    [
        new(
            new Chunk
            {
                Id = "chunk-1",
                DocumentId = "document-1",
                Title = "عنوان آزمایشی",
                Text = text,
                Index = 0
            },
            1.0f)
    ];

    private static string OllamaContent(string content) =>
        JsonSerializer.Serialize(new { message = new { content } });

    private sealed class QueueHttpMessageHandler(params string[] responses) : HttpMessageHandler
    {
        private readonly Queue<string> _responses = new(responses);

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("Unexpected HTTP request in test.");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    _responses.Dequeue(),
                    Encoding.UTF8,
                    "application/json")
            });
        }
    }
}
