namespace Outline.Rag.Domain;

/// <summary>
/// A document as read from Outline. <see cref="Text"/> is Outline's Markdown export of the document body.
/// </summary>
public sealed record SourceDocument(
    Guid Id,
    Guid? CollectionId,
    string Title,
    string Text,
    Uri Url,
    DateTimeOffset UpdatedAt);
