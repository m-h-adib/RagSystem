using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Rag.Application.Abstractions.Documents;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rag.Infrastructure.Documents;

public sealed class WordDocumentParser : IWordDocumentParser
{
	public Task<IReadOnlyList<ParsedBlock>> ParseAsync(
		Stream documentStream,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		using var document = WordprocessingDocument.Open(
			documentStream,
			false);

		var body = document.MainDocumentPart?
			.Document?
			.Body;

		if (body is null)
		{
			return Task.FromResult<IReadOnlyList<ParsedBlock>>([]);
		}

		var result = new List<ParsedBlock>();

		var order = 0;

		foreach (var paragraph in body.Elements<Paragraph>())
		{
			cancellationToken.ThrowIfCancellationRequested();

			var text = ExtractText(paragraph);

			if (string.IsNullOrWhiteSpace(text))
				continue;

			text = Normalize(text);

			var style = GetStyle(
				document.MainDocumentPart,
				paragraph);

			var level = GetHeadingLevel(style);

			// فعلاً TOC را کنار می‌گذاریم.
			if (IsTableOfContentsStyle(style))
				continue;

			result.Add(new ParsedBlock
			{
				Text = text,
				Style = style,
				Level = level,
				Order = order++
			});
		}

		return Task.FromResult<IReadOnlyList<ParsedBlock>>(result);
	}

	private static string ExtractText(
		Paragraph paragraph)
	{
		return string.Concat(
			paragraph
				.Descendants<Text>()
				.Select(x => x.Text));
	}

	private static string Normalize(string text)
	{
		return text
			.Replace('\u00A0', ' ')
			.Replace("\r", " ")
			.Replace("\n", " ")
			.Trim();
	}

	private static string? GetStyle(
		MainDocumentPart? mainPart,
		Paragraph paragraph)
	{
		var styleId = paragraph
			.ParagraphProperties?
			.ParagraphStyleId?
			.Val?
			.Value;

		if (string.IsNullOrWhiteSpace(styleId))
			return null;

		var styles = mainPart?
			.StyleDefinitionsPart?
			.Styles;

		if (styles is null)
			return styleId;

		var style = styles
			.Elements<Style>()
			.FirstOrDefault(x =>
				string.Equals(
					x.StyleId?.Value,
					styleId,
					StringComparison.OrdinalIgnoreCase));

		if (style is null)
			return styleId;

		var styleName = style
			.StyleName?
			.Val?
			.Value;

		return string.IsNullOrWhiteSpace(styleName)
			? styleId
			: styleName;
	}

	private static int GetHeadingLevel(string? style)
	{
		if (string.IsNullOrWhiteSpace(style))
			return 0;

		var normalized = style
			.Replace(" ", string.Empty)
			.Replace("-", string.Empty)
			.Replace("_", string.Empty);

		for (var level = 1; level <= 9; level++)
		{
			if (normalized.Equals(
					$"Heading{level}",
					StringComparison.OrdinalIgnoreCase))
			{
				return level;
			}
		}

		return 0;
	}

	private static bool IsTableOfContentsStyle(string? style)
	{
		if (string.IsNullOrWhiteSpace(style))
			return false;

		var normalized = style
			.Replace(" ", string.Empty)
			.Replace("-", string.Empty)
			.Replace("_", string.Empty);

		return normalized.StartsWith(
			"TOC",
			StringComparison.OrdinalIgnoreCase);
	}
}
