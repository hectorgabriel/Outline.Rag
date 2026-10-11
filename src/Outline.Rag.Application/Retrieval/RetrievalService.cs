using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Outline.Rag.Application.Abstractions;
using Outline.Rag.Application.Ingestion;
using Outline.Rag.Domain;

namespace Outline.Rag.Application.Retrieval;

/// <summary>
/// Finds the chunks that answer a question: vector search, then, when an <see cref="IReranker"/> is configured,
/// a cross-encoder pass that reorders the candidates and decides which are relevant.
/// </summary>
public sealed partial class RetrievalService(
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IChunkIndex index,
    IOptions<RagOptions> options,
    IReranker? reranker = null,
    ILogger<RetrievalService>? logger = null)
{
    private readonly ILogger _logger = logger ?? NullLogger<RetrievalService>.Instance;

    /// <param name="collectionIds">
    /// The collections the caller may read. Empty means the caller can read nothing, not "search everything".
    /// </param>
    public Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
        string query,
        IReadOnlyCollection<Guid> collectionIds,
        int? top,
        CancellationToken cancellationToken) =>
        SearchAsync([query], collectionIds, top, cancellationToken);

    /// <summary>
    /// Searches several phrasings of one question (a follow-up as typed and as rewritten) and merges the results:
    /// a chunk found by more than one keeps its best score. A poor rewrite then cannot hide what the original finds.
    /// The first query is the question itself; it is the one the reranker scores candidates against.
    /// </summary>
    public async Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
        IReadOnlyList<string> queries,
        IReadOnlyCollection<Guid> collectionIds,
        int? top,
        CancellationToken cancellationToken)
    {
        if (collectionIds.Count == 0 || queries.Count == 0)
        {
            return [];
        }

        var rag = options.Value;
        var limit = top ?? rag.TopK;
        var candidates = await FindCandidatesAsync(
                queries, collectionIds, reranker is null ? limit : Math.Max(limit, rag.RerankCandidates), cancellationToken)
            .ConfigureAwait(false);

        if (reranker is not null && candidates.Count > 0)
        {
            try
            {
                var scores = await reranker
                    .ScoreAsync(queries[0], [.. candidates.Select(c => DocumentIngestionService.EmbeddingText(c.Chunk))], cancellationToken)
                    .ConfigureAwait(false);

                return [.. candidates
                    .Zip(scores, (candidate, score) => candidate with { Score = score })
                    .Where(r => r.Score >= rag.RerankMinScore)
                    .OrderByDescending(r => r.Score)
                    .Take(limit)];
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // Search without the reranker rather than fail the question: only the ranking gets worse.
                LogRerankerFailed(_logger, ex);
            }
        }

        return [.. candidates.Where(r => r.Score >= rag.MinScore).Take(limit)];
    }

    /// <returns>Up to <paramref name="perQuery"/> chunks per query, merged by chunk, best vector score first.</returns>
    private async Task<List<RetrievedChunk>> FindCandidatesAsync(
        IReadOnlyList<string> queries,
        IReadOnlyCollection<Guid> collectionIds,
        int perQuery,
        CancellationToken cancellationToken)
    {
        var embeddings = await embeddingGenerator
            .GenerateAsync(queries, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var best = new Dictionary<Guid, RetrievedChunk>();
        foreach (var embedding in embeddings)
        {
            var results = await index
                .SearchAsync(embedding.Vector, perQuery, collectionIds, cancellationToken)
                .ConfigureAwait(false);
            foreach (var result in results)
            {
                if (!best.TryGetValue(result.Chunk.Id, out var seen) || result.Score > seen.Score)
                {
                    best[result.Chunk.Id] = result;
                }
            }
        }

        return [.. best.Values.OrderByDescending(r => r.Score)];
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reranker failed; using the vector search order for this question")]
    private static partial void LogRerankerFailed(ILogger logger, Exception exception);
}
