using Microsoft.Extensions.Options;
using Outline.Rag.Application.Ingestion;
using Outline.Rag.Infrastructure.Persistence;

namespace Outline.Rag.Worker;

/// <summary>
/// Periodically indexes Outline documents changed since the last run. The first run (no checkpoint)
/// indexes everything.
/// </summary>
internal sealed partial class OutlineSyncWorker(
    IServiceScopeFactory scopes,
    RagDatabaseInitializer database,
    IOptions<SyncOptions> options,
    ILogger<OutlineSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await database.InitializeAsync(stoppingToken);

        using var timer = new PeriodicTimer(options.Value.Interval);
        do
        {
#pragma warning disable CA1031 // Keep the worker alive; the next tick retries from the same checkpoint.
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<OutlineSyncService>().SyncChangedAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogSyncFailed(logger, ex);
            }
#pragma warning restore CA1031
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outline sync failed")]
    private static partial void LogSyncFailed(ILogger logger, Exception exception);
}
