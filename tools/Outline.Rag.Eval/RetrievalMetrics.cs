using Outline.Rag.Domain;

namespace Outline.Rag.Eval;

/// <summary>A question and the titles of the documents that answer it; no titles means the wiki has no answer.</summary>
/// <param name="Section">
/// Optional heading that must appear in the chunk's heading path too, for answers that sit in one section of a long
/// document. Without it, any chunk of an expected document counts.
/// </param>
internal sealed record EvalCase(string Question, IReadOnlyList<string> Expected, string? Section = null)
{
    public bool Answerable => Expected.Count > 0;

    public bool IsRelevant(DocumentChunk chunk) =>
        Expected.Any(title => string.Equals(title, chunk.Title, StringComparison.OrdinalIgnoreCase))
        && (Section is null || chunk.HeadingPath.Contains(Section, StringComparison.OrdinalIgnoreCase));
}

internal sealed record EvalDataset(string Name, IReadOnlyList<EvalCase> Cases);

/// <summary>What retrieval returned for one case, before any score cut-off.</summary>
internal sealed record CaseResult(EvalCase Case, IReadOnlyList<RetrievedChunk> Retrieved)
{
    /// <summary>The chunks the answer model would see with <paramref name="minScore"/> applied.</summary>
    public IEnumerable<RetrievedChunk> Kept(double minScore) => Retrieved.Where(r => r.Score >= minScore);

    /// <summary>1-based position of the first chunk from an expected document, or null when none is kept.</summary>
    public int? FirstRelevantRank(double minScore)
    {
        var rank = 0;
        foreach (var chunk in Kept(minScore))
        {
            rank++;
            if (Case.IsRelevant(chunk.Chunk))
            {
                return rank;
            }
        }

        return null;
    }
}

/// <param name="HitAt1">Answerable questions whose first chunk comes from an expected document.</param>
/// <param name="HitAtK">Answerable questions with an expected document anywhere in the kept chunks.</param>
/// <param name="Mrr">Mean reciprocal rank of the first relevant chunk (0 when there is none).</param>
/// <param name="Rejected">Unanswerable questions for which no chunk survives the cut-off, so the model is told there is no answer.</param>
/// <param name="AvgChunksKept">Average number of chunks the answer model receives per answerable question.</param>
internal sealed record ThresholdMetrics(
    double MinScore, double HitAt1, double HitAt3, double HitAtK, double Mrr, double? Rejected, double AvgChunksKept);

internal static class RetrievalMetrics
{
    public static ThresholdMetrics Compute(IReadOnlyList<CaseResult> results, double minScore)
    {
        var answerable = results.Where(r => r.Case.Answerable).ToList();
        var unanswerable = results.Where(r => !r.Case.Answerable).ToList();
        var ranks = answerable.Select(r => r.FirstRelevantRank(minScore)).ToList();

        return new ThresholdMetrics(
            minScore,
            HitAt1: Share(ranks, rank => rank == 1),
            HitAt3: Share(ranks, rank => rank <= 3),
            HitAtK: Share(ranks, rank => rank is not null),
            Mrr: ranks.Count == 0 ? 0 : ranks.Average(rank => rank is { } r ? 1.0 / r : 0),
            Rejected: unanswerable.Count == 0 ? null : Share(unanswerable, r => !r.Kept(minScore).Any()),
            AvgChunksKept: answerable.Count == 0 ? 0 : answerable.Average(r => r.Kept(minScore).Count()));
    }

    private static double Share<T>(IReadOnlyCollection<T> items, Func<T, bool> predicate) =>
        items.Count == 0 ? 0 : (double)items.Count(predicate) / items.Count;
}
