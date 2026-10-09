using System.Text.RegularExpressions;
using Rag.Domain.Entities;

namespace Rag.Infrastructure.Rag;

/// <summary>
/// Conservative lexical gate for LLM-selected context sources.
/// It prevents a source with no meaningful query overlap from entering the answer
/// context merely because the LLM selected it. This is not a semantic model.
/// </summary>
public static class RagSourceRelevanceGuard
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "اگر", "برای", "براي", "این", "آن", "یک", "است", "باشد", "شود", "شده",
        "می", "نیز", "یا", "و", "در", "از", "به", "با", "را", "که", "تا", "هر",
        "هم", "پس", "بعد", "قبل", "روی", "خود", "همان", "باید", "لازم", "دارد",
        "دارند", "چیست", "چگونه", "چطور", "کدام", "چه", "آیا", "آيا", "من", "ما",
        "شما", "آنها", "ایشان", "میشه", "کنم", "کند", "کنند"
    };

    public static bool IsRelevant(string query, Chunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        var queryTerms = GetTerms(query);
        // Short/generic queries do not provide enough lexical signal to safely reject
        // a source; the semantic relevance stage remains in charge for these queries.
        if (queryTerms.Count < 2)
            return true;

        var metadataValues = chunk.Metadata is null
            ? Enumerable.Empty<string>()
            : chunk.Metadata.Values;

        var sourceText = string.Join(
            " ",
            new[] { chunk.Title, chunk.Text }
                .Concat(metadataValues)
                .Where(value => !string.IsNullOrWhiteSpace(value)));

        var sourceTerms = GetTerms(sourceText);
        var matchedTerms = queryTerms.Count(queryTerm =>
            sourceTerms.Any(sourceTerm => Matches(queryTerm, sourceTerm)));

        var coverage = (double)matchedTerms / queryTerms.Count;
        var requiredMatches = Math.Min(2, queryTerms.Count);

        return matchedTerms >= requiredMatches && coverage >= 0.20;
    }

    private static HashSet<string> GetTerms(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new HashSet<string>(StringComparer.Ordinal);

        var normalized = value
            .Replace('ي', 'ی')
            .Replace('ى', 'ی')
            .Replace('ك', 'ک')
            .Replace('ۀ', 'ه')
            .Replace('ة', 'ه')
            .Replace('\u0640', ' ')
            .Replace('\u200c', ' ')
            .ToLowerInvariant();

        return Regex.Matches(normalized, @"[\p{L}\p{Nd}]+")
            .Select(match => match.Value.Trim())
            .Where(term => term.Length >= 3 && !StopWords.Contains(term))
            .SelectMany(ExpandTerm)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IEnumerable<string> ExpandTerm(string term)
    {
        yield return term;

        foreach (var suffix in new[] { "های", "ها", "هایش", "هایی", "ات", "ان", "ین" })
        {
            if (term.Length > suffix.Length + 3 &&
                term.EndsWith(suffix, StringComparison.Ordinal))
            {
                yield return term[..^suffix.Length];
                yield break;
            }
        }
    }

    private static bool Matches(string queryTerm, string sourceTerm)
    {
        if (string.Equals(queryTerm, sourceTerm, StringComparison.Ordinal))
            return true;

        return queryTerm.Length >= 5 &&
               sourceTerm.Length >= 5 &&
               (queryTerm.StartsWith(sourceTerm, StringComparison.Ordinal) ||
                sourceTerm.StartsWith(queryTerm, StringComparison.Ordinal));
    }
}
