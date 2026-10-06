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
    public async Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
        string query,
        IReadOnlyCollection<Guid> collectionIds,
        int? top,
        CancellationToken cancellationToken)
    {
        if (collectionIds.Count == 0)
        {
            return [];
        }

        var embedding = await embeddingGenerator
            .GenerateVectorAsync(query, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var results = await index
            .SearchAsync(embedding, top ?? options.Value.TopK, collectionIds, cancellationToken)
            .ConfigureAwait(false);

        return [.. results.Where(r => r.Score >= options.Value.MinScore)];
    }
}
