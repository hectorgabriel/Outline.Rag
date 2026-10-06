using Outline.Rag.Domain;

namespace Outline.Rag.Application.Abstractions;

/// <summary>
/// Vector index of document chunks.
/// </summary>
public interface IChunkIndex
{
    Task EnsureCreatedAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Replaces every indexed chunk of <paramref name="documentId"/> with <paramref name="chunks"/>.
    /// </summary>
    Task ReplaceDocumentAsync(
        Guid documentId,
        IReadOnlyList<(DocumentChunk Chunk, ReadOnlyMemory<float> Embedding)> chunks,
        CancellationToken cancellationToken);

    Task DeleteDocumentAsync(Guid documentId, CancellationToken cancellationToken);

    /// <param name="collectionIds">
    /// Only chunks from these Outline collections are returned; empty returns nothing, so a caller with no
    /// readable collections can never fall through to an unfiltered search.
    /// </param>
    Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
        ReadOnlyMemory<float> queryEmbedding,
        int top,
        IReadOnlyCollection<Guid> collectionIds,
        CancellationToken cancellationToken);
}
