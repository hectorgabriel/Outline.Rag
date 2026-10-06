using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using NSubstitute;
using Outline.Rag.Application;
using Outline.Rag.Application.Abstractions;
using Outline.Rag.Application.Retrieval;

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
}
