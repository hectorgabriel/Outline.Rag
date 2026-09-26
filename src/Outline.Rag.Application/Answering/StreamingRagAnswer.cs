using Outline.Rag.Domain;

namespace Outline.Rag.Application.Answering;

/// <summary>
/// A streamed <see cref="RagAnswer"/>: the citations are known before the first token, the text is not.
/// </summary>
/// <param name="Parts">Reasoning and answer fragments in the order the model writes them; enumerate once.</param>
public sealed record StreamingRagAnswer(IAsyncEnumerable<AnswerPart> Parts, IReadOnlyList<Citation> Citations);

/// <param name="Kind">Whether <paramref name="Text"/> is the model's reasoning or part of the answer.</param>
public sealed record AnswerPart(AnswerPartKind Kind, string Text);

public enum AnswerPartKind
{
    Answer,

    /// <summary>The model thinking before it answers. Not part of the answer and may not cite anything.</summary>
    Reasoning,
}
