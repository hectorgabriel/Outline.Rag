using Outline.Rag.Application.Abstractions;

namespace Outline.Rag.Infrastructure.Persistence;

/// <summary>
/// Creates the RAG tables (pgvector extension must already be installed in the database).
/// </summary>
public sealed class RagDatabaseInitializer
{
    private readonly IChunkIndex _index;
    private readonly NpgsqlSyncCheckpointStore _checkpoints;
    private readonly NpgsqlIndexedDocumentStore _ingestedDocuments;

    internal RagDatabaseInitializer(
        IChunkIndex index, NpgsqlSyncCheckpointStore checkpoints, NpgsqlIndexedDocumentStore ingestedDocuments)
    {
        _index = index;
        _checkpoints = checkpoints;
        _ingestedDocuments = ingestedDocuments;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _checkpoints.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        await _ingestedDocuments.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        await _index.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
    }
}
