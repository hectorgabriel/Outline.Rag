using Outline.Rag.Domain;

namespace Outline.Rag.UnitTests;

public sealed class ChunkIdTests
{
    [Fact]
    public void For_IsDeterministicAndDistinctPerIndex()
    {
        var doc = Guid.NewGuid();

        Assert.Equal(ChunkId.For(doc, 0), ChunkId.For(doc, 0));
        Assert.NotEqual(ChunkId.For(doc, 0), ChunkId.For(doc, 1));
    }
}
