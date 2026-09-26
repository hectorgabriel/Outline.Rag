using Outline.Rag.Domain;

namespace Outline.Rag.UnitTests;

internal static class TestData
{
    public static SourceDocument Document(string text, Guid? collectionId = null) => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        collectionId,
        "Runbook",
        text,
        new Uri("https://wiki.example/doc/runbook-abc"),
        new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));

    public static DocumentChunk Chunk(int index, string content, string headingPath = "") =>
        new(Document("").Id, null, index, "Runbook", headingPath, content, new Uri("https://wiki.example/doc/runbook-abc"), default);
}
