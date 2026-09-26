using Microsoft.Extensions.Logging;
using Outline.Rag.Application.Abstractions;

namespace Outline.Rag.Application.Ingestion;

/// <summary>
/// Pulls documents changed since the last checkpoint from Outline and (re)indexes them.
/// </summary>
/// <remarks>
/// Deletions are not visible in the list endpoint; they arrive through Outline webhooks
/// (see <see cref="SyncDocumentAsync"/>).
/// </remarks>
public sealed partial class OutlineSyncService(
    IOutlineDocumentSource source,
    DocumentIngestionService ingestion,
    ISyncCheckpointStore checkpoints,
    ILogger<OutlineSyncService> logger)
{
    public async Task<int> SyncChangedAsync(CancellationToken cancellationToken)
    {
        var since = await checkpoints.GetLastSyncedAsync(cancellationToken).ConfigureAwait(false);
        DateTimeOffset? newest = null;
        var count = 0;

        await foreach (var document in source.ListDocumentsAsync(since, cancellationToken).ConfigureAwait(false))
        {
            // One bad document must not stall the whole sync.
#pragma warning disable CA1031
            try
            {
                await ingestion.IngestAsync(document, cancellationToken).ConfigureAwait(false);
                count++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogIngestFailed(logger, ex, document.Id);
            }
#pragma warning restore CA1031

            if (newest is null || document.UpdatedAt > newest)
            {
                newest = document.UpdatedAt;
            }
        }

        if (newest is not null)
        {
            await checkpoints.SetLastSyncedAsync(newest.Value, cancellationToken).ConfigureAwait(false);
        }

        LogSyncCompleted(logger, count, since);
        return count;
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
}
