using Rag.Application.Abstractions.Documents;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rag.Infrastructure.Documents;

public sealed class DocumentStructureBuilder : IDocumentStructureBuilder
{
	public IReadOnlyList<StructuredSegment> Build(
		IReadOnlyList<ParsedBlock> blocks)
	{
		var result = new List<StructuredSegment>();

		var headingStack = new Dictionary<int, string>();

		var currentText = new StringBuilder();

		var currentMetadata = new Dictionary<string, string>();

		var order = 0;

		foreach (var block in blocks)
		{
			if (block.Level > 0)
			{
				FlushCurrentSegment(
					result,
					currentText,
					currentMetadata,
					ref order);

				UpdateHeadingStack(
					headingStack,
					block.Level,
					block.Text);

				currentMetadata =
					CreateMetadata(headingStack);

				continue;
			}

			if (string.IsNullOrWhiteSpace(block.Text))
				continue;

			if (currentText.Length > 0)
				currentText.AppendLine();

			currentText.Append(block.Text);
		}

		FlushCurrentSegment(
			result,
			currentText,
			currentMetadata,
			ref order);

		return result;
	}

	private static void UpdateHeadingStack(
		Dictionary<int, string> headingStack,
		int level,
		string text)
	{
		headingStack[level] = text;

		var levelsToRemove = headingStack.Keys
			.Where(x => x > level)
			.ToList();

		foreach (var item in levelsToRemove)
			headingStack.Remove(item);
	}

	private static Dictionary<string, string> CreateMetadata(
		Dictionary<int, string> headingStack)
	{
		var metadata = new Dictionary<string, string>();

		foreach (var item in headingStack.OrderBy(x => x.Key))
		{
			metadata[$"heading{item.Key}"] = item.Value;
		}

		return metadata;
	}

	private static void FlushCurrentSegment(
		List<StructuredSegment> result,
		StringBuilder text,
		Dictionary<string, string> metadata,
		ref int order)
	{
		if (text.Length == 0)
			return;

		var content = text.ToString().Trim();

		if (string.IsNullOrWhiteSpace(content))
		{
			text.Clear();
			return;
		}

		result.Add(new StructuredSegment
		{
			Text = content,
			Metadata = metadata.Count == 0
				? null
				: new Dictionary<string, string>(metadata),
			Order = order++
		});

		text.Clear();
	}
}
