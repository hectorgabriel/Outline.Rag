namespace Outline.Rag.Application.Abstractions;

/// <summary>
/// Answers "which Outline collections may this person read?", so retrieval only searches those.
/// </summary>
public interface ICollectionAccessResolver
{
    /// <returns>
    /// The readable collection ids; empty when the email matches no active Outline user or the user can read nothing.
    /// </returns>
    Task<IReadOnlySet<Guid>> GetReadableCollectionIdsAsync(string email, CancellationToken cancellationToken);
}
