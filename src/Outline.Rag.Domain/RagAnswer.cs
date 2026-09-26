namespace Outline.Rag.Domain;

public sealed record RagAnswer(string Answer, IReadOnlyList<Citation> Citations);

/// <param name="Number">The [n] marker used in the answer text.</param>
public sealed record Citation(int Number, Guid DocumentId, string Title, string HeadingPath, Uri Url, double Score);
