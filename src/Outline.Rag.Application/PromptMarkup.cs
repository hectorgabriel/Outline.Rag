using System.Text.RegularExpressions;

namespace Outline.Rag.Application;

/// <summary>
/// Keeps untrusted text (wiki content, earlier answers) inside the XML-like tags that prompts wrap it in. Anyone who
/// can edit a wiki page controls that text, so it must not be able to close the wrapper and pose as instructions.
/// </summary>
internal static partial class PromptMarkup
{
    /// <summary>Escapes a value for a double-quoted attribute.</summary>
    public static string Attribute(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);

    /// <summary>
    /// Escapes the opening bracket of any tag named in <paramref name="tagNames"/> (opening or closing, any case),
    /// and leaves every other character alone, so Markdown, code and HTML reach the model unchanged.
    /// </summary>
    public static string Body(string text, params string[] tagNames) =>
        TagStart().Replace(text, m =>
            tagNames.Contains(m.Groups["name"].Value, StringComparer.OrdinalIgnoreCase) ? "&lt;" + m.Value[1..] : m.Value);

    [GeneratedRegex(@"<\s*/?\s*(?<name>[A-Za-z][\w-]*)")]
    private static partial Regex TagStart();
}
