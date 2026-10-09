using Rag.Domain.Entities;

namespace Rag.Infrastructure.Embeddings;

/// <summary>
/// Builds the same enriched text for indexing and reranking so titles and section metadata
/// participate in retrieval rather than being lost when only the chunk body is used.
/// </summary>
public static class ChunkSearchTextBuilder
{
    public static string Build(Chunk chunk)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(chunk.Title))
            parts.Add($"عنوان: {chunk.Title.Trim()}");

        if (chunk.Metadata is { Count: > 0 })
        {
            foreach (var item in chunk.Metadata
                         .Where(x => !string.IsNullOrWhiteSpace(x.Value))
                         .OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                parts.Add($"{item.Key}: {item.Value.Trim()}");
            }
        }

        if (!string.IsNullOrWhiteSpace(chunk.Text))
            parts.Add($"متن: {chunk.Text.Trim()}");

        return string.Join("\n", parts.Distinct(StringComparer.Ordinal));
    }
}
