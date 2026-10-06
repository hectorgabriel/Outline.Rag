using Outline.Rag.Domain;

namespace Outline.Rag.Application.Abstractions;

/// <summary>
/// Read-only access to Outline documents (implemented over the Outline HTTP API).
/// </summary>
public interface IOutlineDocumentSource
{
    /// <summary>
    /// Streams published documents, most recently updated first, stopping once documents are
    /// older than <paramref name="updatedSince"/> (when given; documents updated exactly then are included).
    /// </summary>
    /// <remarks>
    /// Best effort: Outline pages by offset over <c>updatedAt</c>, which is not unique, so documents sharing a
    /// timestamp can be listed twice or skipped. <see cref="ListDocumentIdsAsync"/> is the complete list.
    /// </remarks>
    IAsyncEnumerable<SourceDocument> ListDocumentsAsync(DateTimeOffset? updatedSince, CancellationToken cancellationToken);

    /// <summary>
    /// Ids of every published document visible to the API token, read from the collections' document trees.
    /// </summary>
    Task<IReadOnlySet<Guid>> ListDocumentIdsAsync(CancellationToken cancellationToken);

    /// <returns>The document, or <see langword="null"/> if it no longer exists or is not visible to the API token.</returns>
    Task<SourceDocument?> GetDocumentAsync(Guid documentId, CancellationToken cancellationToken);
}
