using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using NSubstitute;
using Outline.Rag.Application;
using Outline.Rag.Application.Abstractions;
using Outline.Rag.Application.Retrieval;
using Outline.Rag.Domain;

namespace Outline.Rag.UnitTests.Application;

public sealed class RetrievalServiceTests
{
    [Fact]
    public async Task SearchAsync_WithNoReadableCollections_ReturnsNothingWithoutSearching()
    {
        var embeddings = Substitute.For<IEmbeddingGenerator<string, Embedding<float>>>();
        var index = Substitute.For<IChunkIndex>();
        var service = new RetrievalService(embeddings, index, Options.Create(new RagOptions()));

        var results = await service.SearchAsync("vacation", [], top: null, TestContext.Current.CancellationToken);

        Assert.Empty(results);
        await embeddings.DidNotReceive().GenerateAsync(
            Arg.Any<IEnumerable<string>>(), Arg.Any<EmbeddingGenerationOptions?>(), Arg.Any<CancellationToken>());
        await index.DidNotReceive().SearchAsync(
            Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_SeveralQueries_MergesByChunkKeepingBestScore()
    {
        var embeddings = Substitute.For<IEmbeddingGenerator<string, Embedding<float>>>();
        var index = Substitute.For<IChunkIndex>();
        embeddings.GenerateAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EmbeddingGenerationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new GeneratedEmbeddings<Embedding<float>>(
                [new Embedding<float>(new float[] { 1, 0 }), new Embedding<float>(new float[] { 0, 1 })]));
        index.SearchAsync(Arg.Is<ReadOnlyMemory<float>>(v => v.ToArray()[0] == 1), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new RetrievedChunk(TestData.Chunk(0, "a"), 0.6), new RetrievedChunk(TestData.Chunk(1, "b"), 0.55)]);
        index.SearchAsync(Arg.Is<ReadOnlyMemory<float>>(v => v.ToArray()[1] == 1), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new RetrievedChunk(TestData.Chunk(1, "b"), 0.9), new RetrievedChunk(TestData.Chunk(2, "c"), 0.4)]);
        var service = new RetrievalService(embeddings, index, Options.Create(new RagOptions { MinScore = 0.5 }));

        var results = await service.SearchAsync(["rewritten", "as typed"], [Guid.NewGuid()], top: 2, TestContext.Current.CancellationToken);

        Assert.Equal([(1, 0.9), (0, 0.6)], results.Select(r => (r.Chunk.Index, r.Score)));
    }
}
