using Outline.Rag.Domain;

namespace Outline.Rag.Application.Answering;

/// <summary>
/// A streamed <see cref="RagAnswer"/>: the citations are known before the first token, the text is not.
/// </summary>
/// <param name="Text">Answer text fragments in order; enumerate once.</param>
public sealed record StreamingRagAnswer(IAsyncEnumerable<string> Text, IReadOnlyList<Citation> Citations);
