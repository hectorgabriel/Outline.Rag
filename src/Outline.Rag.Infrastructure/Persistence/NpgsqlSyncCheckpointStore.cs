using Npgsql;
using Outline.Rag.Application.Abstractions;

namespace Outline.Rag.Infrastructure.Persistence;

/// <summary>
/// Stores the sync high-water mark in the RAG database (never in Outline's database).
/// </summary>
internal sealed class NpgsqlSyncCheckpointStore(NpgsqlDataSource dataSource) : ISyncCheckpointStore
{
    private const string Key = "outline.documents";

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken)
    {
        await using var cmd = dataSource.CreateCommand(
            "CREATE TABLE IF NOT EXISTS rag_sync_state (key text PRIMARY KEY, value timestamptz NOT NULL)");
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<DateTimeOffset?> GetLastSyncedAsync(CancellationToken cancellationToken)
    {
        await using var cmd = dataSource.CreateCommand("SELECT value FROM rag_sync_state WHERE key = $1");
        cmd.Parameters.AddWithValue(Key);
        var value = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is DateTime dt ? new DateTimeOffset(dt, TimeSpan.Zero) : null;
    }

    public async Task SetLastSyncedAsync(DateTimeOffset value, CancellationToken cancellationToken)
    {
        await using var cmd = dataSource.CreateCommand(
            "INSERT INTO rag_sync_state (key, value) VALUES ($1, $2) ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value");
        cmd.Parameters.AddWithValue(Key);
        cmd.Parameters.AddWithValue(value.UtcDateTime);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
