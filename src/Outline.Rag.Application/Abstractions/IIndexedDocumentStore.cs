namespace Outline.Rag.Application.Abstractions;

/// <summary>
/// Ledger of the Outline documents already ingested and the version (<c>updatedAt</c>) each was ingested at.
/// Documents that produced no chunks are recorded too, so they are not fetched again on every sync.
/// </summary>
public interface IIndexedDocumentStore
{
    Task<IReadOnlyDictionary<Guid, DateTimeOffset>> GetAllAsync(CancellationToken cancellationToken);

    Task SetAsync(Guid documentId, DateTimeOffset updatedAt, CancellationToken cancellationToken);

    Task RemoveAsync(Guid documentId, CancellationToken cancellationToken);
}
