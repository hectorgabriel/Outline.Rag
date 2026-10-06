using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Outline.Rag.Application;
using Outline.Rag.Application.Abstractions;
using Outline.Rag.Application.Ingestion;
using Outline.Rag.Domain;

namespace Outline.Rag.UnitTests.Application;

public sealed class OutlineSyncServiceTests
{
    private static readonly DateTimeOffset MigratedAt = new(2026, 9, 3, 22, 6, 28, 132, TimeSpan.Zero);

    private readonly IOutlineDocumentSource _source = Substitute.For<IOutlineDocumentSource>();
    private readonly IDocumentChunker _chunker = Substitute.For<IDocumentChunker>();
    private readonly IChunkIndex _index = Substitute.For<IChunkIndex>();
    private readonly ISyncCheckpointStore _checkpoints = Substitute.For<ISyncCheckpointStore>();
    private readonly IIndexedDocumentStore _ingested = Substitute.For<IIndexedDocumentStore>();

    public OutlineSyncServiceTests()
    {
        // Documents without chunks keep the test off the embedding path; they are still ingested and recorded.
        _chunker.Chunk(Arg.Any<SourceDocument>()).Returns([]);
        _ingested.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, DateTimeOffset>());
        _source.ListDocumentIdsAsync(Arg.Any<CancellationToken>()).Returns(new HashSet<Guid>());
    }

    private OutlineSyncService CreateService() => new(
        _source,
        new DocumentIngestionService(
            _chunker,
            Substitute.For<IEmbeddingGenerator<string, Embedding<float>>>(),
            _index,
            _ingested,
            Options.Create(new RagOptions()),
            NullLogger<DocumentIngestionService>.Instance),
        _checkpoints,
        _ingested,
        NullLogger<OutlineSyncService>.Instance);

    private static SourceDocument Document(Guid id, DateTimeOffset updatedAt) =>
        new(id, null, "Doc", "", new Uri($"https://wiki.example/doc/{id}"), updatedAt);

    private void List(params SourceDocument[] documents) =>
        _source.ListDocumentsAsync(Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>()).Returns(documents.ToAsyncEnumerable());

    private void Tree(params Guid[] ids) =>
        _source.ListDocumentIdsAsync(Arg.Any<CancellationToken>()).Returns(ids.ToHashSet());

    [Fact]
    public async Task SyncChangedAsync_DocumentListedTwice_IngestsOnce()
    {
        var doc = Document(Guid.NewGuid(), MigratedAt);
        List(doc, doc);
        Tree(doc.Id);

        var count = await CreateService().SyncChangedAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, count);
        await _ingested.Received(1).SetAsync(doc.Id, MigratedAt, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncChangedAsync_UnchangedDocumentAtCheckpoint_IsNotReingested()
    {
        var doc = Document(Guid.NewGuid(), MigratedAt);
        _ingested.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, DateTimeOffset> { [doc.Id] = MigratedAt });
        List(doc);
        Tree(doc.Id);

        var count = await CreateService().SyncChangedAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, count);
        await _ingested.DidNotReceive().SetAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncChangedAsync_DocumentSkippedByListing_IsFetchedAndIngested()
    {
        var listed = Document(Guid.NewGuid(), MigratedAt);
        var skipped = Document(Guid.NewGuid(), MigratedAt);
        List(listed);
        Tree(listed.Id, skipped.Id);
        _source.GetDocumentAsync(skipped.Id, Arg.Any<CancellationToken>()).Returns(skipped);

        var count = await CreateService().SyncChangedAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, count);
        await _source.DidNotReceive().GetDocumentAsync(listed.Id, Arg.Any<CancellationToken>());
        await _ingested.Received(1).SetAsync(skipped.Id, MigratedAt, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncChangedAsync_DocumentGoneFromOutline_IsRemovedFromIndex()
    {
        var kept = Guid.NewGuid();
        var deleted = Guid.NewGuid();
        _ingested.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
            new Dictionary<Guid, DateTimeOffset> { [kept] = MigratedAt, [deleted] = MigratedAt });
        List();
        Tree(kept);

        var count = await CreateService().SyncChangedAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, count);
        await _index.Received(1).DeleteDocumentAsync(deleted, Arg.Any<CancellationToken>());
        await _ingested.Received(1).RemoveAsync(deleted, Arg.Any<CancellationToken>());
        await _index.DidNotReceive().DeleteDocumentAsync(kept, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncChangedAsync_SavesNewestTimestampAsCheckpoint()
    {
        var newest = MigratedAt.AddDays(1);
        List(Document(Guid.NewGuid(), newest), Document(Guid.NewGuid(), MigratedAt));

        await CreateService().SyncChangedAsync(TestContext.Current.CancellationToken);

        await _checkpoints.Received(1).SetLastSyncedAsync(newest, Arg.Any<CancellationToken>());
    }
}
