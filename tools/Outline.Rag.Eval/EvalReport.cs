using System.Globalization;

namespace Outline.Rag.Eval;

internal static class EvalReport
{
    public static void Write(
        TextWriter output,
        EvalDataset dataset,
        IReadOnlyList<CaseResult> results,
        int top,
        double configuredMinScore,
        IEnumerable<double> thresholds,
        TimeSpan elapsed)
    {
        var answerable = results.Count(r => r.Case.Answerable);
        Line(output, $"Dataset '{dataset.Name}': {answerable} answerable and {results.Count - answerable} unanswerable questions, top {top} chunks.");
        Line(output, $"Retrieval took {elapsed.TotalMilliseconds / Math.Max(1, results.Count):F0} ms per question on average.");
        output.WriteLine();

        Line(output, $"{"MinScore",9} {"Hit@1",7} {"Hit@3",7} {$"Hit@{top}",7} {"MRR",6} {"Rejected",9} {"Chunks",7}");
        foreach (var minScore in thresholds.Append(configuredMinScore).Distinct().Order())
        {
            var m = RetrievalMetrics.Compute(results, minScore);
            var marker = minScore.Equals(configuredMinScore) ? " <- configured" : "";
            var rejected = m.Rejected is { } r ? Percent(r) : "-";
            Line(output, $"{m.MinScore,9:F2} {Percent(m.HitAt1),7} {Percent(m.HitAt3),7} {Percent(m.HitAtK),7} {m.Mrr,6:F3} {rejected,9} {m.AvgChunksKept,7:F1}{marker}");
        }

        output.WriteLine();
        output.WriteLine("Hit@n: answerable questions with a chunk of an expected document in the first n. Rejected: unanswerable");
        output.WriteLine("questions with no chunk above MinScore. Chunks: average kept per answerable question.");

        WriteMisses(output, results, configuredMinScore);
        WarnAboutUnseenTitles(output, results);
    }

    private static void WriteMisses(TextWriter output, IReadOnlyList<CaseResult> results, double minScore)
    {
        var misses = results
            .Where(r => r.Case.Answerable ? r.FirstRelevantRank(minScore) != 1 : r.Kept(minScore).Any())
            .ToList();
        if (misses.Count == 0)
        {
            return;
        }

        output.WriteLine();
        Line(output, $"Misses at MinScore {minScore:F2} (expected document not first, or unanswerable question not rejected):");
        foreach (var miss in misses)
        {
            var rank = miss.FirstRelevantRank(minScore);
            var expected = !miss.Case.Answerable ? "(nothing)"
                : string.Join(" | ", miss.Case.Expected) + (miss.Case.Section is { } section ? $" > {section}" : "");
            var where = miss.Case.Answerable ? rank is { } n ? $"found at #{n}" : "not found" : "not rejected";
            output.WriteLine();
            Line(output, $"  Q: {miss.Case.Question}");
            Line(output, $"     expected {expected}, {where}");
            foreach (var (chunk, i) in miss.Retrieved.Take(3).Select((c, i) => (c, i)))
            {
                var mark = miss.Case.IsRelevant(chunk.Chunk) ? "*" : " ";
                Line(output, $"   {mark} {i + 1}. {chunk.Score:F3}  {chunk.Chunk.Title} > {chunk.Chunk.HeadingPath}");
            }
        }
    }

    /// <summary>An expected title that never comes back at all is more likely a typo in the dataset than a retrieval miss.</summary>
    private static void WarnAboutUnseenTitles(TextWriter output, IReadOnlyList<CaseResult> results)
    {
        var seen = results.SelectMany(r => r.Retrieved).Select(r => r.Chunk.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unseen = results.SelectMany(r => r.Case.Expected).Where(t => !seen.Contains(t)).Distinct().ToList();
        if (unseen.Count > 0)
        {
            output.WriteLine();
            Line(output, $"Warning: never retrieved, check the dataset spelling: {string.Join(", ", unseen)}");
        }
    }

    private static string Percent(double value) => value.ToString("P0", CultureInfo.InvariantCulture);

    private static void Line(TextWriter output, FormattableString text) =>
        output.WriteLine(text.ToString(CultureInfo.InvariantCulture));
}
