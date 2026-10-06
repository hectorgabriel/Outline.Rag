using Microsoft.Extensions.Logging;
using Outline.Rag.Application.Abstractions;
using Outline.Rag.Domain;

namespace Outline.Rag.Application.Ingestion;

/// <summary>
/// Pulls documents changed since the last checkpoint from Outline and (re)indexes them, then reconciles the
/// index with the complete list of documents so nothing the listing skipped, and nothing deleted, lingers.
/// </summary>
/// <remarks>
/// Outline's list endpoint pages by offset over <c>updatedAt</c>, which bulk updates (e.g. an upgrade migration)
/// leave identical on many documents, so a page can repeat or skip some of them. The reconciliation step fetches
/// the skipped ones. Deletions also arrive through Outline webhooks (see <see cref="SyncDocumentAsync"/>).
/// </remarks>
public sealed partial class OutlineSyncService(
    IOutlineDocumentSource source,
    DocumentIngestionService ingestion,
    ISyncCheckpointStore checkpoints,
    IIndexedDocumentStore ingestedDocuments,
    ILogger<OutlineSyncService> logger)
{
    public async Task<int> SyncChangedAsync(CancellationToken cancellationToken)
    {
        var since = await checkpoints.GetLastSyncedAsync(cancellationToken).ConfigureAwait(false);
        var ingested = new Dictionary<Guid, DateTimeOffset>(
            await ingestedDocuments.GetAllAsync(cancellationToken).ConfigureAwait(false));
        DateTimeOffset? newest = null;
        var count = 0;

        await foreach (var document in source.ListDocumentsAsync(since, cancellationToken).ConfigureAwait(false))
        {
            if (newest is null || document.UpdatedAt > newest)
            {
                newest = document.UpdatedAt;
            }

            // Listed twice by unstable paging, or re-read because it shares the checkpoint's timestamp.
            if (ingested.TryGetValue(document.Id, out var ingestedAt) && ingestedAt == document.UpdatedAt)
            {
                continue;
            }

            if (await TryIngestAsync(document, cancellationToken).ConfigureAwait(false))
            {
                ingested[document.Id] = document.UpdatedAt;
                count++;
            }
        }

        if (newest is not null)
        {
            await checkpoints.SetLastSyncedAsync(newest.Value, cancellationToken).ConfigureAwait(false);
        }

        LogSyncCompleted(logger, count, since);
        return count + await ReconcileAsync(ingested, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Ingests documents Outline has but the index lacks, and removes the ones Outline no longer has.
    /// </summary>
    private async Task<int> ReconcileAsync(Dictionary<Guid, DateTimeOffset> ingested, CancellationToken cancellationToken)
    {
        var current = await source.ListDocumentIdsAsync(cancellationToken).ConfigureAwait(false);
        var added = 0;
        foreach (var documentId in current.Where(id => !ingested.ContainsKey(id)))
        {
            var document = await source.GetDocumentAsync(documentId, cancellationToken).ConfigureAwait(false);
            if (document is not null && await TryIngestAsync(document, cancellationToken).ConfigureAwait(false))
            {
                added++;
            }
        }

        var removed = 0;
        foreach (var documentId in ingested.Keys.Where(id => !current.Contains(id)))
        {
            await ingestion.RemoveAsync(documentId, cancellationToken).ConfigureAwait(false);
            removed++;
        }

        LogReconciled(logger, added, removed);
        return added + removed;
    }

    private async Task<bool> TryIngestAsync(SourceDocument document, CancellationToken cancellationToken)
    {
        // One bad document must not stall the whole sync.
#pragma warning disable CA1031
        try
        {
            await ingestion.IngestAsync(document, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogIngestFailed(logger, ex, document.Id);
            return false;
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Re-reads one document from Outline; removes it from the index if it is gone.
    /// </summary>
    public async Task SyncDocumentAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var document = await source.GetDocumentAsync(documentId, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            await ingestion.RemoveAsync(documentId, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await ingestion.IngestAsync(document, cancellationToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to ingest document {DocumentId}")]
    private static partial void LogIngestFailed(ILogger logger, Exception exception, Guid documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Outline sync indexed {Count} documents changed since {Since}")]
    private static partial void LogSyncCompleted(ILogger logger, int count, DateTimeOffset? since);

    [LoggerMessage(Level = LogLevel.Information, Message = "Outline sync reconciled the index: {Added} missing documents added, {Removed} removed")]
    private static partial void LogReconciled(ILogger logger, int added, int removed);
}
