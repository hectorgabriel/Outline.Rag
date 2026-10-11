using Outline.Rag.Domain;
using Outline.Rag.Eval;

namespace Outline.Rag.UnitTests.Eval;

public sealed class RetrievalMetricsTests
{
    private static RetrievedChunk Hit(string title, double score, string headingPath = "") =>
        new(TestData.Chunk(0, "text", headingPath) with { Title = title }, score);

    [Fact]
    public void FirstRelevantRank_CountsOnlyChunksAboveMinScore()
    {
        var result = new CaseResult(
            new EvalCase("q", ["Benefits"]),
            [Hit("Other", 0.9), Hit("Other", 0.7), Hit("Benefits", 0.6)]);

        Assert.Equal(3, result.FirstRelevantRank(0.5));
        Assert.Null(result.FirstRelevantRank(0.65));
    }

    [Fact]
    public void IsRelevant_WithSection_RequiresMatchingHeadingPath()
    {
        var evalCase = new EvalCase("q", ["Benefits & Perks"], Section: "sabbatical");

        Assert.True(evalCase.IsRelevant(Hit("benefits & perks", 1, "Paid Time Off > Sabbatical").Chunk));
        Assert.False(evalCase.IsRelevant(Hit("Benefits & Perks", 1, "Paid Time Off").Chunk));
        Assert.False(evalCase.IsRelevant(Hit("Severance", 1, "Sabbatical").Chunk));
    }

    [Fact]
    public void Compute_ScoresAnswerableAndUnanswerableSeparately()
    {
        CaseResult[] results =
        [
            new(new EvalCase("first", ["A"]), [Hit("A", 0.8)]),
            new(new EvalCase("second", ["A"]), [Hit("B", 0.8), Hit("A", 0.6)]),
            new(new EvalCase("missed", ["A"]), [Hit("B", 0.8)]),
            new(new EvalCase("off-topic", []), [Hit("B", 0.4)]),
            new(new EvalCase("off-topic too", []), [Hit("B", 0.7)]),
        ];

        var m = RetrievalMetrics.Compute(results, minScore: 0.5);

        Assert.Equal(1.0 / 3, m.HitAt1, 3);
        Assert.Equal(2.0 / 3, m.HitAt3, 3);
        Assert.Equal(2.0 / 3, m.HitAtK, 3);
        Assert.Equal((1 + 0.5 + 0) / 3, m.Mrr, 3);
        Assert.Equal(0.5, m.Rejected);
        Assert.Equal(4.0 / 3, m.AvgChunksKept, 3);
    }

    [Fact]
    public void Compute_WithoutUnanswerableCases_ReportsNoRejectionRate()
    {
        var m = RetrievalMetrics.Compute([new CaseResult(new EvalCase("q", ["A"]), [Hit("A", 0.9)])], minScore: 0);

        Assert.Null(m.Rejected);
        Assert.Equal(1, m.Mrr);
    }
}
