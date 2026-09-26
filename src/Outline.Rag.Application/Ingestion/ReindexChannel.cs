using System.Threading.Channels;

namespace Outline.Rag.Application.Ingestion;

/// <summary>
/// In-process queue of documents to re-read from Outline (fed by webhooks, drained by a background service).
/// Lost on restart; the periodic sync in the Worker catches anything missed except deletions.
/// </summary>
public sealed class ReindexChannel
{
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(
        new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public bool Enqueue(Guid documentId) => _channel.Writer.TryWrite(documentId);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
