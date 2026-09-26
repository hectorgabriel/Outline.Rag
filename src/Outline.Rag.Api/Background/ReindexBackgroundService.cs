using Outline.Rag.Application.Ingestion;

namespace Outline.Rag.Api.Background;

internal sealed partial class ReindexBackgroundService(
    ReindexChannel queue,
    IServiceScopeFactory scopes,
    ILogger<ReindexBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var documentId in queue.ReadAllAsync(stoppingToken))
        {
#pragma warning disable CA1031 // A failed document must not stop the loop.
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<OutlineSyncService>().SyncDocumentAsync(documentId, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogReindexFailed(logger, ex, documentId);
            }
#pragma warning restore CA1031
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Re-indexing document {DocumentId} failed")]
    private static partial void LogReindexFailed(ILogger logger, Exception exception, Guid documentId);
}
