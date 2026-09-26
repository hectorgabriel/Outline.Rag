namespace Outline.Rag.Application.Abstractions;

/// <summary>
/// Persists the high-water mark of the incremental Outline sync.
/// </summary>
public interface ISyncCheckpointStore
{
    Task<DateTimeOffset?> GetLastSyncedAsync(CancellationToken cancellationToken);

    Task SetLastSyncedAsync(DateTimeOffset value, CancellationToken cancellationToken);
}
