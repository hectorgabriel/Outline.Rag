namespace Outline.Rag.Application.Abstractions;

/// <summary>
/// Scores how well each passage answers a query, reading both together (a cross-encoder). More precise than
/// embedding similarity, but too slow to run over the whole index, so it reorders the vector search's candidates.
/// </summary>
public interface IReranker
{
    /// <returns>One relevance score per passage, in the order given, from 0 (unrelated) to 1 (answers it).</returns>
    Task<IReadOnlyList<double>> ScoreAsync(string query, IReadOnlyList<string> passages, CancellationToken cancellationToken);
}
