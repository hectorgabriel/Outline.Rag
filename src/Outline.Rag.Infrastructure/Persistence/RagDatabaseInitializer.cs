using Npgsql;
using Outline.Rag.Application.Abstractions;

namespace Outline.Rag.Infrastructure.Persistence;

/// <summary>
/// Creates the RAG tables (pgvector extension must already be installed in the database).
/// </summary>
/// <remarks>
/// The Api and the Worker both run this at startup. <c>CREATE TABLE IF NOT EXISTS</c> is not safe against a concurrent
/// run of itself: both sessions see no table, and the slower one fails on the system catalog (23505 on
/// <c>pg_type_typname_nsp_index</c>, or 42P07). A session-level advisory lock makes the runs take turns; the later
/// ones then find every table in place.
/// </remarks>
public sealed class RagDatabaseInitializer
{
    /// <summary>Arbitrary application-wide key for <c>pg_advisory_lock</c>; only needs to be unique in this database.</summary>
    internal const long AdvisoryLockKey = 0x5241_475F_494E_4954; // "RAG_INIT"

    private readonly NpgsqlDataSource _dataSource;
    private readonly IChunkIndex _index;
    private readonly NpgsqlSyncCheckpointStore _checkpoints;
    private readonly NpgsqlIndexedDocumentStore _ingestedDocuments;

    internal RagDatabaseInitializer(
        NpgsqlDataSource dataSource,
        IChunkIndex index,
        NpgsqlSyncCheckpointStore checkpoints,
        NpgsqlIndexedDocumentStore ingestedDocuments)
    {
        _dataSource = dataSource;
        _index = index;
        _checkpoints = checkpoints;
        _ingestedDocuments = ingestedDocuments;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        // The lock belongs to this connection's session, so it is held while the steps below run on other pooled
        // connections, and Postgres releases it if the process dies before the unlock.
        await using var lockConnection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(lockConnection, "SELECT pg_advisory_lock($1)", cancellationToken).ConfigureAwait(false);
        try
        {
            await _checkpoints.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
            await _ingestedDocuments.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
            await _index.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await ExecuteAsync(lockConnection, "SELECT pg_advisory_unlock($1)", CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue(AdvisoryLockKey);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
