using Npgsql;
using Outline.Rag.Application.Abstractions;

namespace Outline.Rag.Infrastructure.Persistence;

/// <summary>
/// Stores which Outline documents were ingested, and at which version, in the RAG database.
/// </summary>
internal sealed class NpgsqlIndexedDocumentStore(NpgsqlDataSource dataSource) : IIndexedDocumentStore
{
    public async Task EnsureCreatedAsync(CancellationToken cancellationToken)
    {
        await using var cmd = dataSource.CreateCommand(
            "CREATE TABLE IF NOT EXISTS rag_documents (document_id uuid PRIMARY KEY, updated_at timestamptz NOT NULL)");
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<Guid, DateTimeOffset>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var cmd = dataSource.CreateCommand("SELECT document_id, updated_at FROM rag_documents");
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var documents = new Dictionary<Guid, DateTimeOffset>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            documents[reader.GetGuid(0)] = new DateTimeOffset(reader.GetDateTime(1), TimeSpan.Zero);
        }

        return documents;
    }

    public async Task SetAsync(Guid documentId, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        await using var cmd = dataSource.CreateCommand(
            "INSERT INTO rag_documents (document_id, updated_at) VALUES ($1, $2) " +
            "ON CONFLICT (document_id) DO UPDATE SET updated_at = EXCLUDED.updated_at");
        cmd.Parameters.AddWithValue(documentId);
        cmd.Parameters.AddWithValue(updatedAt.UtcDateTime);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid documentId, CancellationToken cancellationToken)
    {
        await using var cmd = dataSource.CreateCommand("DELETE FROM rag_documents WHERE document_id = $1");
        cmd.Parameters.AddWithValue(documentId);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
