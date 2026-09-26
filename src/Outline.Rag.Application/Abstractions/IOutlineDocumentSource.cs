using Outline.Rag.Domain;

namespace Outline.Rag.Application.Abstractions;

/// <summary>
/// Read-only access to Outline documents (implemented over the Outline HTTP API).
/// </summary>
public interface IOutlineDocumentSource
{
    /// <summary>
    /// Streams published documents, most recently updated first, stopping once documents are
    /// older than <paramref name="updatedSince"/> (when given).
    /// </summary>
    IAsyncEnumerable<SourceDocument> ListDocumentsAsync(DateTimeOffset? updatedSince, CancellationToken cancellationToken);

    /// <returns>The document, or <see langword="null"/> if it no longer exists or is not visible to the API token.</returns>
    Task<SourceDocument?> GetDocumentAsync(Guid documentId, CancellationToken cancellationToken);
}
