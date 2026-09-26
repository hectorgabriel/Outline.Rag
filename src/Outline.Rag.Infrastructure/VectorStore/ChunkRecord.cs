using Microsoft.Extensions.VectorData;
using Outline.Rag.Domain;

namespace Outline.Rag.Infrastructure.VectorStore;

/// <summary>
/// Storage shape of a <see cref="DocumentChunk"/>. The schema is declared in <see cref="ChunkRecordDefinition"/>
/// rather than with attributes so the vector dimension can come from configuration.
/// </summary>
internal sealed class ChunkRecord
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }

    /// <summary><see cref="Guid.Empty"/> when the document has no collection.</summary>
    public Guid CollectionId { get; set; }

    public int ChunkIndex { get; set; }
    public string Title { get; set; } = "";
    public string HeadingPath { get; set; } = "";
    public string Content { get; set; } = "";
    public string Url { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
    public ReadOnlyMemory<float> Embedding { get; set; }

    public static ChunkRecord From(DocumentChunk chunk, ReadOnlyMemory<float> embedding) => new()
    {
        Id = chunk.Id,
        DocumentId = chunk.DocumentId,
        CollectionId = chunk.CollectionId ?? Guid.Empty,
        ChunkIndex = chunk.Index,
        Title = chunk.Title,
        HeadingPath = chunk.HeadingPath,
        Content = chunk.Content,
        Url = chunk.Url.ToString(),
        UpdatedAt = chunk.UpdatedAt,
        Embedding = embedding,
    };

    public DocumentChunk ToDomain() => new(
        DocumentId,
        CollectionId == Guid.Empty ? null : CollectionId,
        ChunkIndex,
        Title,
        HeadingPath,
        Content,
        new Uri(Url),
        UpdatedAt);
}

internal static class ChunkRecordDefinition
{
    public static VectorStoreCollectionDefinition Create(int dimensions) => new()
    {
        Properties =
        [
            new VectorStoreKeyProperty(nameof(ChunkRecord.Id), typeof(Guid)),
            new VectorStoreDataProperty(nameof(ChunkRecord.DocumentId), typeof(Guid)) { IsIndexed = true },
            new VectorStoreDataProperty(nameof(ChunkRecord.CollectionId), typeof(Guid)) { IsIndexed = true },
            new VectorStoreDataProperty(nameof(ChunkRecord.ChunkIndex), typeof(int)),
            new VectorStoreDataProperty(nameof(ChunkRecord.Title), typeof(string)),
            new VectorStoreDataProperty(nameof(ChunkRecord.HeadingPath), typeof(string)),
            new VectorStoreDataProperty(nameof(ChunkRecord.Content), typeof(string)),
            new VectorStoreDataProperty(nameof(ChunkRecord.Url), typeof(string)),
            new VectorStoreDataProperty(nameof(ChunkRecord.UpdatedAt), typeof(DateTimeOffset)),
            new VectorStoreVectorProperty(nameof(ChunkRecord.Embedding), typeof(ReadOnlyMemory<float>), dimensions)
            {
                DistanceFunction = DistanceFunction.CosineDistance,
                IndexKind = IndexKind.Hnsw,
            },
        ],
    };
}
