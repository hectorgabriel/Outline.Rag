using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Outline.Rag.Application.Abstractions;
using Outline.Rag.Domain;

namespace Outline.Rag.Application.Ingestion;

/// <summary>
/// Chunks, embeds and indexes single documents.
/// </summary>
public sealed partial class DocumentIngestionService(
    IDocumentChunker chunker,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IChunkIndex index,
    IIndexedDocumentStore ingested,
    IOptions<RagOptions> options,
    ILogger<DocumentIngestionService> logger)
{
    public async Task IngestAsync(SourceDocument document, CancellationToken cancellationToken)
    {
        var chunks = chunker.Chunk(document);
        if (chunks.Count == 0)
        {
            await index.DeleteDocumentAsync(document.Id, cancellationToken).ConfigureAwait(false);
            await ingested.SetAsync(document.Id, document.UpdatedAt, cancellationToken).ConfigureAwait(false);
            return;
        }

        var embedded = new List<(DocumentChunk, ReadOnlyMemory<float>)>(chunks.Count);
        foreach (var batch in chunks.Chunk(options.Value.EmbeddingBatchSize))
        {
            var embeddings = await embeddingGenerator
                .GenerateAsync(batch.Select(EmbeddingText), cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            embedded.AddRange(batch.Zip(embeddings, (chunk, e) => (chunk, e.Vector)));
        }

        await index.ReplaceDocumentAsync(document.Id, embedded, cancellationToken).ConfigureAwait(false);
        await ingested.SetAsync(document.Id, document.UpdatedAt, cancellationToken).ConfigureAwait(false);
        LogIndexed(logger, document.Id, document.Title, chunks.Count);
    }

    public async Task RemoveAsync(Guid documentId, CancellationToken cancellationToken)
    {
        await index.DeleteDocumentAsync(documentId, cancellationToken).ConfigureAwait(false);
        await ingested.RemoveAsync(documentId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Title and heading path are prepended so chunks keep their context once separated from the document.
    /// </summary>
    internal static string EmbeddingText(DocumentChunk chunk) =>
        string.IsNullOrEmpty(chunk.HeadingPath)
            ? $"{chunk.Title}\n\n{chunk.Content}"
            : $"{chunk.Title} > {chunk.HeadingPath}\n\n{chunk.Content}";

    [LoggerMessage(Level = LogLevel.Information, Message = "Indexed document {DocumentId} '{Title}' as {ChunkCount} chunks")]
    private static partial void LogIndexed(ILogger logger, Guid documentId, string title, int chunkCount);
}
