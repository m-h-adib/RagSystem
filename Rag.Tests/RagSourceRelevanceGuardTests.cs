using Rag.Domain.Entities;
using Rag.Infrastructure.Rag;

namespace Rag.Tests;

public sealed class RagSourceRelevanceGuardTests
{
    [Fact]
    public void ExcludesMenstruationChunkFromIstihadaTawafQuestion()
    {
        var query = "وظیفه مستحاضه در طواف و نماز طواف چیست؟";
        var chunk = new Chunk
        {
            Title = "مسئله2: نقاء متخلل",
            Text = "اگر سه روز يا بيشتر خون ببيند و پاک شود و دوباره قبل از ده روز خون ببيند؛ روزهاي پاکي وسط نيز حيض است."
        };

        Assert.False(RagSourceRelevanceGuard.IsRelevant(query, chunk));
    }

    [Theory]
    [InlineData("وظیفه مستحاضه در طواف و نماز طواف چیست؟", "مسئله6: وظیفه مستحاضه قلیله نسبت به وضو در طواف و نماز آن", "برای هر کدام از طواف و نماز وضوی مستقل نیاز دارد.")]
    [InlineData("وظیفه مستحاضه در طواف چیست؟", "مسئله3: وظیفه مستحاضه نسبت به تطهیر بدن و لباس در طواف", "ظاهر فرج را تطهیر کند و پنبه را تطهیر یا تعویض نماید.")]
    public void KeepsChunkWithMeaningfulQueryOverlap(string query, string title, string text)
    {
        var chunk = new Chunk { Title = title, Text = text };

        Assert.True(RagSourceRelevanceGuard.IsRelevant(query, chunk));
    }

    [Fact]
    public void DoesNotRejectVeryShortQueryBasedOnLexicalSignalAlone()
    {
        var chunk = new Chunk { Title = "عنوان نامرتبط", Text = "متن نامرتبط" };

        Assert.True(RagSourceRelevanceGuard.IsRelevant("حکم؟", chunk));
    }
}
