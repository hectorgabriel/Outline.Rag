namespace Outline.Rag.Domain;

/// <summary>
/// A retrievable slice of a <see cref="SourceDocument"/>.
/// </summary>
/// <param name="HeadingPath">Markdown headings enclosing the chunk, outermost first (e.g. "Setup > Database").</param>
public sealed record DocumentChunk(
    Guid DocumentId,
    Guid? CollectionId,
    int Index,
    string Title,
    string HeadingPath,
    string Content,
    Uri Url,
    DateTimeOffset UpdatedAt)
{
    /// <summary>
    /// Deterministic key so re-indexing a document overwrites its previous chunks.
    /// </summary>
    public Guid Id => ChunkId.For(DocumentId, Index);
}
