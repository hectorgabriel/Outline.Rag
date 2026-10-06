using CommunityToolkit.VectorData.PgVector;
using Microsoft.Extensions.VectorData;
using Outline.Rag.Application.Abstractions;
using Outline.Rag.Domain;

namespace Outline.Rag.Infrastructure.VectorStore;

/// <summary>
/// <see cref="IChunkIndex"/> over PostgreSQL + pgvector, via the Microsoft.Extensions.VectorData abstractions.
/// Swapping to another vector database means replacing the collection passed in here.
/// </summary>
internal sealed class PgVectorChunkIndex(PostgresCollection<Guid, ChunkRecord> collection) : IChunkIndex
{
    // Upper bound on chunks per document when looking up a document's existing keys.
    private const int MaxChunksPerDocument = 10_000;

    public Task EnsureCreatedAsync(CancellationToken cancellationToken) =>
        collection.EnsureCollectionExistsAsync(cancellationToken);

    public async Task ReplaceDocumentAsync(
        Guid documentId,
        IReadOnlyList<(DocumentChunk Chunk, ReadOnlyMemory<float> Embedding)> chunks,
        CancellationToken cancellationToken)
    {
        // Keys are deterministic per (document, index): upsert first, then drop leftovers from a longer
        // previous version, so the document never disappears from search mid-update.
        await collection
            .UpsertAsync(chunks.Select(c => ChunkRecord.From(c.Chunk, c.Embedding)), cancellationToken)
            .ConfigureAwait(false);

        var current = chunks.Select(c => c.Chunk.Id).ToHashSet();
        var stale = (await GetKeysAsync(documentId, cancellationToken).ConfigureAwait(false))
            .Where(id => !current.Contains(id))
            .ToList();

        if (stale.Count > 0)
        {
            await collection.DeleteAsync(stale, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task DeleteDocumentAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var keys = await GetKeysAsync(documentId, cancellationToken).ConfigureAwait(false);
        if (keys.Count > 0)
        {
            await collection.DeleteAsync(keys, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
        ReadOnlyMemory<float> queryEmbedding,
        int top,
        IReadOnlyCollection<Guid> collectionIds,
        CancellationToken cancellationToken)
    {
        if (collectionIds.Count == 0)
        {
            return [];
        }

        var ids = collectionIds.ToArray();
        var options = new VectorSearchOptions<ChunkRecord> { Filter = r => ids.Contains(r.CollectionId) };

        var results = new List<RetrievedChunk>(top);
        await foreach (var result in collection.SearchAsync(queryEmbedding, top, options, cancellationToken).ConfigureAwait(false))
        {
            // CosineDistance: 0 = identical. Report similarity so higher is better.
            results.Add(new RetrievedChunk(result.Record.ToDomain(), 1 - (result.Score ?? 1)));
        }

        return results;
    }

    private async Task<List<Guid>> GetKeysAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var keys = new List<Guid>();
        await foreach (var record in collection
            .GetAsync(r => r.DocumentId == documentId, MaxChunksPerDocument, cancellationToken: cancellationToken)
            .ConfigureAwait(false))
        {
            keys.Add(record.Id);
        }

        return keys;
    }
}
