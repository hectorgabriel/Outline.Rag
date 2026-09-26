using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.ML.Tokenizers;
using Outline.Rag.Application;
using Outline.Rag.Application.Abstractions;
using Outline.Rag.Domain;

namespace Outline.Rag.Infrastructure.Chunking;

/// <summary>
/// Splits Outline Markdown on headings, then on paragraphs, then on token boundaries, so every chunk fits
/// <see cref="RagOptions.MaxChunkTokens"/>. Overlap is applied only when a single paragraph has to be cut.
/// </summary>
/// <remarks>
/// Token counts use cl100k_base as an approximation; the embedding model's own tokenizer may differ slightly,
/// which is why the default chunk size stays well below typical embedding context limits.
/// </remarks>
internal sealed class MarkdownHeadingChunker(Tokenizer tokenizer, IOptions<RagOptions> options) : IDocumentChunker
{
    public IReadOnlyList<DocumentChunk> Chunk(SourceDocument document)
    {
        var max = options.Value.MaxChunkTokens;
        var overlap = Math.Min(options.Value.ChunkOverlapTokens, max / 2);
        var chunks = new List<DocumentChunk>();

        foreach (var (headingPath, body) in SplitSections(document.Text))
        {
            foreach (var piece in SplitToFit(body, max, overlap))
            {
                chunks.Add(new DocumentChunk(
                    document.Id, document.CollectionId, chunks.Count, document.Title, headingPath,
                    piece, document.Url, document.UpdatedAt));
            }
        }

        return chunks;
    }

    /// <summary>Yields (heading path, section body) pairs, ignoring '#' lines inside fenced code blocks.</summary>
    internal static IEnumerable<(string HeadingPath, string Body)> SplitSections(string markdown)
    {
        var headings = new List<(int Level, string Text)>();
        var body = new StringBuilder();
        var inFence = false;

        foreach (var line in markdown.ReplaceLineEndings("\n").Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
            }

            var level = inFence ? 0 : HeadingLevel(trimmed);
            if (level == 0)
            {
                body.AppendLine(line);
                continue;
            }

            if (Flush() is { } section)
            {
                yield return section;
            }

            headings.RemoveAll(h => h.Level >= level);
            headings.Add((level, trimmed[level..].Trim()));
        }

        if (Flush() is { } last)
        {
            yield return last;
        }

        (string, string)? Flush()
        {
            var text = body.ToString().Trim();
            body.Clear();
            return text.Length == 0 ? null : (string.Join(" > ", headings.Select(h => h.Text)), text);
        }
    }

    private static int HeadingLevel(string line)
    {
        var level = 0;
        while (level < line.Length && line[level] == '#')
        {
            level++;
        }

        return level is >= 1 and <= 6 && level < line.Length && line[level] == ' ' ? level : 0;
    }

    private IEnumerable<string> SplitToFit(string text, int max, int overlap)
    {
        if (tokenizer.CountTokens(text) <= max)
        {
            yield return text;
            yield break;
        }

        var current = new StringBuilder();
        var currentTokens = 0;
        foreach (var paragraph in text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var tokens = tokenizer.CountTokens(paragraph);
            if (currentTokens + tokens > max && current.Length > 0)
            {
                yield return current.ToString().Trim();
                current.Clear();
                currentTokens = 0;
            }

            if (tokens > max)
            {
                foreach (var slice in SliceByTokens(paragraph, max, overlap))
                {
                    yield return slice;
                }

                continue;
            }

            current.Append(paragraph).Append("\n\n");
            currentTokens += tokens;
        }

        if (current.Length > 0)
        {
            yield return current.ToString().Trim();
        }
    }

    private IEnumerable<string> SliceByTokens(string text, int max, int overlap)
    {
        var start = 0;
        while (start < text.Length)
        {
            var remaining = text[start..];
            var length = tokenizer.GetIndexByTokenCount(remaining, max, out _, out _);
            if (length <= 0)
            {
                yield break;
            }

            yield return remaining[..length].Trim();
            if (start + length >= text.Length)
            {
                yield break;
            }

            // Step back by `overlap` tokens so neighbouring slices share context.
            var overlapChars = overlap == 0
                ? 0
                : length - tokenizer.GetIndexByTokenCountFromEnd(remaining[..length], overlap, out _, out _);
            start += Math.Max(1, length - overlapChars);
        }
    }
}
