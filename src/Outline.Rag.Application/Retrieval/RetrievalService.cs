using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Outline.Rag.Application.Abstractions;
using Outline.Rag.Domain;

namespace Outline.Rag.Application.Retrieval;

public sealed class RetrievalService(
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IChunkIndex index,
    IOptions<RagOptions> options)
{
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

        var limit = top ?? options.Value.TopK;
        var embeddings = await embeddingGenerator
            .GenerateAsync(queries, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var best = new Dictionary<Guid, RetrievedChunk>();
        foreach (var embedding in embeddings)
        {
            var results = await index
                .SearchAsync(embedding.Vector, limit, collectionIds, cancellationToken)
                .ConfigureAwait(false);
            foreach (var result in results)
            {
                if (!best.TryGetValue(result.Chunk.Id, out var seen) || result.Score > seen.Score)
                {
                    best[result.Chunk.Id] = result;
                }
            }
        }

        return [.. best.Values
            .Where(r => r.Score >= options.Value.MinScore)
            .OrderByDescending(r => r.Score)
            .Take(limit)];
    }
}
