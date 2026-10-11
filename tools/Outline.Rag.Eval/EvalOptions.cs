using System.ComponentModel.DataAnnotations;

namespace Outline.Rag.Eval;

internal sealed class EvalOptions
{
    public const string SectionName = "Eval";

    public static readonly Dictionary<string, string> SwitchMappings = new(StringComparer.Ordinal)
    {
        ["--dataset"] = $"{SectionName}:{nameof(Dataset)}",
        ["--email"] = $"{SectionName}:{nameof(Email)}",
        ["--top"] = $"{SectionName}:{nameof(Top)}",
        ["--rewrite"] = $"{SectionName}:{nameof(Rewrite)}",
        ["--reranker"] = "AI:Reranker:Provider",
    };

    /// <summary>Dataset file; relative paths are tried against the current directory, then the tool's output directory.</summary>
    [Required]
    public string Dataset { get; set; } = "datasets/handbook.json";

    /// <summary>Outline user to evaluate as: retrieval is scoped to the collections this person can read.</summary>
    [Required, EmailAddress]
    public string Email { get; set; } = "";

    /// <summary>Chunks retrieved per question; defaults to <c>Rag:TopK</c>, i.e. what the answer model sees.</summary>
    [Range(1, 50)]
    public int? Top { get; set; }

    /// <summary>
    /// Rewrite follow-ups into standalone questions with the chat model, as the chat endpoint does. False searches
    /// the follow-up as typed, for a baseline. Cases without history never call the model.
    /// </summary>
    public bool Rewrite { get; set; } = true;

    /// <summary>Score cut-offs to report besides the configured one.</summary>
    /// <remarks>The low values are for reranker scores, which spread over 0-1 unlike cosine similarities.</remarks>
    public IReadOnlyList<double> Thresholds { get; set; } = [0.0, 0.01, 0.05, 0.1, 0.2, 0.3, 0.4, 0.45, 0.5, 0.55, 0.6, 0.65, 0.7];
}
