using System.Globalization;
using System.Text.RegularExpressions;

namespace Rag.Infrastructure.Documents;

/// <summary>
/// Splits a chunk only when it contains a clearly sequential, line-start numbered list.
/// The text before the first list item is retained with each item as shared context.
/// This is format-driven and does not depend on book topics, names, or question types.
/// </summary>
public static class NumberedListChunkSplitter
{
    private static readonly Regex ItemMarker = new(
        @"(?m)^[\t ]*(?<number>[0-9۰-۹٠-٩]{1,3})[.)．、][\t ]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<NumberedListPart> Split(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var matches = ItemMarker.Matches(text);
        if (matches.Count < 2)
            return [new NumberedListPart(text.Trim(), null)];

        var numbers = matches
            .Select(match => ParseNumber(match.Groups["number"].Value))
            .ToArray();

        // Avoid splitting arbitrary numeric references or lists that don't look
        // like one continuous enumeration beginning at 1.
        if (numbers[0] != 1 ||
            numbers.Where((number, index) => number != index + 1).Any())
        {
            return [new NumberedListPart(text.Trim(), null)];
        }

        var prefix = text[..matches[0].Index].Trim();
        var parts = new List<NumberedListPart>(matches.Count);

        for (var index = 0; index < matches.Count; index++)
        {
            var start = matches[index].Index;
            var end = index + 1 < matches.Count
                ? matches[index + 1].Index
                : text.Length;

            var itemText = text[start..end].Trim();
            if (!string.IsNullOrWhiteSpace(prefix))
                itemText = $"{prefix}{Environment.NewLine}{itemText}";

            parts.Add(new NumberedListPart(itemText, numbers[index]));
        }

        return parts;
    }

    private static int ParseNumber(string value)
    {
        var ascii = string.Concat(value.Select(character => character switch
        {
            >= '۰' and <= '۹' => (char)('0' + character - '۰'),
            >= '٠' and <= '٩' => (char)('0' + character - '٠'),
            _ => character
        }));

        return int.Parse(ascii, NumberStyles.None, CultureInfo.InvariantCulture);
    }
}

public sealed record NumberedListPart(string Text, int? ListItemNumber);
