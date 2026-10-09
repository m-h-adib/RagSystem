using Rag.Infrastructure.Documents;

namespace Rag.Tests;

public sealed class NumberedListChunkSplitterTests
{
    [Theory]
    [InlineData("1. حکم اول\n2. حکم دوم", 2)]
    [InlineData("عنوان بخش\n\n۱. حکم اول\n\n۲. حکم دوم", 2)]
    [InlineData("عنوان بخش\n\n١. حکم اول\n\n٢. حکم دوم", 2)]
    public void Split_SequentialNumberedParagraphs_CreatesOnePartPerItem(
        string text,
        int expectedCount)
    {
        var result = NumberedListChunkSplitter.Split(text);

        Assert.Equal(expectedCount, result.Count);
        Assert.All(result, item => Assert.False(string.IsNullOrWhiteSpace(item.Text)));
        Assert.Equal(Enumerable.Range(1, expectedCount), result.Select(x => x.ListItemNumber!.Value));
    }

    [Fact]
    public void Split_PreservesSharedIntroInEachPart()
    {
        const string text = "مسئله: قربانی\n1. حکم اول\n2. حکم دوم";

        var result = NumberedListChunkSplitter.Split(text);

        Assert.Equal(2, result.Count);
        Assert.All(result, item => Assert.Contains("مسئله: قربانی", item.Text));
        Assert.Contains("حکم اول", result[0].Text);
        Assert.Contains("حکم دوم", result[1].Text);
    }

    [Fact]
    public void Split_NonSequentialNumbers_LeavesChunkUntouched()
    {
        const string text = "اشاره به موارد\n1. حکم اول\n3. حکم سوم";

        var result = NumberedListChunkSplitter.Split(text);

        Assert.Single(result);
        Assert.Null(result[0].ListItemNumber);
        Assert.Equal(text, result[0].Text);
    }

    [Fact]
    public void Split_SingleItem_LeavesChunkUntouched()
    {
        const string text = "1. فقط یک بند";

        var result = NumberedListChunkSplitter.Split(text);

        Assert.Single(result);
        Assert.Null(result[0].ListItemNumber);
        Assert.Equal(text, result[0].Text);
    }
}
