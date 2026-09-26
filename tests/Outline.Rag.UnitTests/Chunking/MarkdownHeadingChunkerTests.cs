using Microsoft.Extensions.Options;
using Microsoft.ML.Tokenizers;
using Outline.Rag.Application;
using Outline.Rag.Infrastructure.Chunking;

namespace Outline.Rag.UnitTests.Chunking;

public sealed class MarkdownHeadingChunkerTests
{
    private static readonly Tokenizer Tokenizer = TiktokenTokenizer.CreateForModel("gpt-4");

    private static MarkdownHeadingChunker CreateChunker(int maxTokens = 512, int overlap = 0) =>
        new(Tokenizer, Options.Create(new RagOptions { MaxChunkTokens = maxTokens, ChunkOverlapTokens = overlap }));

    [Fact]
    public void Chunk_TracksNestedHeadingPath()
    {
        const string markdown = """
            Intro text.

            # Setup
            Install things.

            ## Database
            Restore the backup.

            # Usage
            Run it.
            """;

        var chunks = CreateChunker().Chunk(TestData.Document(markdown));

        Assert.Equal(
            [("", "Intro text."), ("Setup", "Install things."), ("Setup > Database", "Restore the backup."), ("Usage", "Run it.")],
            chunks.Select(c => (c.HeadingPath, c.Content)));
        Assert.Equal([0, 1, 2, 3], chunks.Select(c => c.Index));
    }

    [Fact]
    public void Chunk_IgnoresHashLinesInsideCodeFences()
    {
        const string markdown = """
            # Script
            ```bash
            # not a heading
            echo hi
            ```
            """;

        var chunk = Assert.Single(CreateChunker().Chunk(TestData.Document(markdown)));

        Assert.Equal("Script", chunk.HeadingPath);
        Assert.Contains("# not a heading", chunk.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Chunk_SkipsHeadingsWithoutBody()
    {
        var chunks = CreateChunker().Chunk(TestData.Document("# Empty\n## Also empty\n"));

        Assert.Empty(chunks);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    public void Chunk_SplitsOversizedSectionsWithinTokenLimit(int overlap)
    {
        const int max = 64;
        var paragraph = string.Join(' ', Enumerable.Repeat("La base de datos se restaura desde el respaldo.", 40));
        var markdown = $"# Big\n{paragraph}\n\nShort closing paragraph.";

        var chunks = CreateChunker(max, overlap).Chunk(TestData.Document(markdown));

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(Tokenizer.CountTokens(c.Content) <= max, $"chunk {c.Index} too large"));
        Assert.All(chunks, c => Assert.Equal("Big", c.HeadingPath));
        Assert.Equal("Short closing paragraph.", chunks[^1].Content);
    }
}
