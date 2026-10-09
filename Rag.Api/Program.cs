using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Models;
using Qdrant.Client;
using Rag.Application.Abstractions.Documents;
using Rag.Application.Abstractions.Embeddings;
using Rag.Application.Abstractions.Llm;
using Rag.Application.Abstractions.Rag;
using Rag.Application.Abstractions.Reranking;
using Rag.Application.Abstractions.VectorStore;
using Rag.Infrastructure.Documents;
using Rag.Infrastructure.Embeddings.BgeM3;
using Rag.Infrastructure.Ollama;
using Rag.Infrastructure.Rag;
using Rag.Infrastructure.Reranking;
using Rag.Infrastructure.VectorStore.Qdrant;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// Services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Swagger
builder.Services.AddSwaggerGen(options =>
{
	options.SwaggerDoc(
		"v1",
		new OpenApiInfo
		{
			Title = "RAG API",
			Version = "v1",
			Description = "RAG System API"
		});
});

// Ollama Options
builder.Services
	.AddOptions<OllamaOptions>()
	.Bind(builder.Configuration.GetSection("Ollama"))
	.Validate(
		options => Uri.TryCreate(
			options.BaseUrl,
			UriKind.Absolute,
			out _),
		"Ollama BaseUrl is invalid.")
	.ValidateOnStart();

// Ollama Semantic Chunking
builder.Services.AddHttpClient<
	ISemanticChunkingService,
	OllamaSemanticChunkingService>(
	(serviceProvider, client) =>
	{
		var options =
			serviceProvider
				.GetRequiredService<
					Microsoft.Extensions.Options.IOptions<OllamaOptions>>()
				.Value;

		client.BaseAddress = new Uri(options.BaseUrl);

		client.Timeout = TimeSpan.FromMinutes(10);
	});

builder.Services.AddHttpClient<ILlmService, OllamaLlmService>(
	(serviceProvider, client) =>
{
var options =
	serviceProvider
		.GetRequiredService<IOptions<OllamaOptions>>()
		.Value;

client.BaseAddress = new Uri(options.BaseUrl);
client.Timeout = TimeSpan.FromMinutes(10);
});

و:

///bge3
builder.Services
	.AddOptions<BgeM3Options>()
	.Bind(builder.Configuration.GetSection("BgeM3"))
	.Validate(
		options => Uri.TryCreate(
			options.BaseUrl,
			UriKind.Absolute,
			out _),
		"BGE-M3 BaseUrl is invalid.")
	.Validate(
        options => options.Dimension > 0,
        "BGE-M3 Dimension must be greater than zero.")
    .Validate(
        options => options.MaxConcurrentRequests is >= 1 and <= 4,
        "BGE-M3 MaxConcurrentRequests must be between 1 and 4.")
    .ValidateOnStart();

builder.Services.AddHttpClient<
	IEmbeddingService,
	BgeM3EmbeddingService>(
	(serviceProvider, client) =>
	{
		var options =
			serviceProvider
				.GetRequiredService<
					Microsoft.Extensions.Options.IOptions<BgeM3Options>>()
				.Value;

		client.BaseAddress = new Uri(options.BaseUrl);
		client.Timeout = TimeSpan.FromMinutes(5);
	});


///qdrant
builder.Services
	.AddOptions<QdrantOptions>()
	.Bind(builder.Configuration.GetSection("Qdrant"))
	.Validate(
		options => !string.IsNullOrWhiteSpace(options.Host),
		"Qdrant Host is required.")
	.Validate(
		options => options.GrpcPort is > 0 and <= 65535,
		"Qdrant gRPC port is invalid.")
	.Validate(
		options => !string.IsNullOrWhiteSpace(options.CollectionName),
		"Qdrant collection name is required.")
	.Validate(
		options => options.VectorSize > 0,
		"Qdrant vector size must be greater than zero.")
	.ValidateOnStart();

builder.Services.AddSingleton<QdrantClient>(serviceProvider =>
{
	var options = serviceProvider
		.GetRequiredService<IOptions<QdrantOptions>>()
		.Value;

	return new QdrantClient(
		host: options.Host,
		port: options.GrpcPort);
});

builder.Services.AddScoped<IVectorStore, QdrantVectorStore>();



builder.Services.AddScoped<IDocumentIngestionService, DocumentIngestionService>();



///reranker
builder.Services
    .AddOptions<BgeRerankerOptions>()
	.Bind(builder.Configuration.GetSection("BgeReranker"))
	.Validate(
		options => Uri.TryCreate(
			options.BaseUrl,
			UriKind.Absolute,
			out _),
		"BGE Reranker BaseUrl is invalid.")
	.ValidateOnStart();

builder.Services.AddHttpClient<IRerankerService, BgeRerankerService>(
	(serviceProvider, client) =>
	{
		var options =
			serviceProvider
				.GetRequiredService<IOptions<BgeRerankerOptions>>()
				.Value;

		client.BaseAddress = new Uri(options.BaseUrl);
		client.Timeout = TimeSpan.FromMinutes(5);
	});


///rag
builder.Services
	.AddOptions<RagOptions>()
	.Bind(builder.Configuration.GetSection("Rag"))
	.Validate(
        options => options.ContextCount > 0,
        "Rag ContextCount must be greater than zero.")
    .Validate(
        options => options.CandidateCount is >= 10 and <= 100,
        "Rag CandidateCount must be between 10 and 100.")
    .ValidateOnStart();

builder.Services.AddHttpClient<IRagAnswerService, RagAnswerService>(
	(serviceProvider, client) =>
	{
		var options = serviceProvider
			.GetRequiredService<IOptions<OllamaOptions>>()
			.Value;

		client.BaseAddress = new Uri(options.BaseUrl);
		client.Timeout = TimeSpan.FromMinutes(10);
	});



///parser
builder.Services.AddScoped<
	IWordDocumentParser,
	WordDocumentParser>();

builder.Services.AddScoped<
	IDocumentStructureBuilder,
	DocumentStructureBuilder>();

builder.Services.AddScoped<
	IStructuredDocumentChunkingService,
	StructuredDocumentChunkingService>();

builder.Services.AddScoped<
	IWordDocumentIngestionService,
	WordDocumentIngestionService>();

builder.Services.AddScoped<IChunkImportService, ChunkImportService>();

var app = builder.Build();

// Swagger
app.UseSwagger();

app.UseSwaggerUI(options =>
{
	options.SwaggerEndpoint(
		"/swagger/v1/swagger.json",
		"RAG API v1");

	options.RoutePrefix = "swagger";
});

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();