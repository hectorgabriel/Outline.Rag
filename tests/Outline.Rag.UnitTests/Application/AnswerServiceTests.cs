using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using NSubstitute;
using Outline.Rag.Application;
using Outline.Rag.Application.Abstractions;
using Outline.Rag.Application.Answering;
using Outline.Rag.Application.Retrieval;
using Outline.Rag.Domain;

namespace Outline.Rag.UnitTests.Application;

public sealed class AnswerServiceTests
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddings = Substitute.For<IEmbeddingGenerator<string, Embedding<float>>>();
    private readonly IChunkIndex _index = Substitute.For<IChunkIndex>();
    private readonly IChatClient _chat = Substitute.For<IChatClient>();

    public AnswerServiceTests()
    {
        _embeddings
            .GenerateAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EmbeddingGenerationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new GeneratedEmbeddings<Embedding<float>>([new Embedding<float>(new float[] { 1, 0 })]));
    }

    private AnswerService CreateService() =>
        new(new RetrievalService(_embeddings, _index, Options.Create(new RagOptions { MinScore = 0.5 })), _chat);

    [Fact]
    public async Task AskAsync_WithoutRelevantChunks_DoesNotCallModel()
    {
        _index.SearchAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new RetrievedChunk(TestData.Chunk(0, "irrelevant"), 0.1)]);

        var answer = await CreateService().AskAsync("¿Qué es esto?", [], TestContext.Current.CancellationToken);

        Assert.Empty(answer.Citations);
        await _chat.DidNotReceive().GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AskAsync_NumbersCitationsInRetrievalOrder()
    {
        _index.SearchAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(
        [
            new RetrievedChunk(TestData.Chunk(0, "Restore with pg_restore.", "Database"), 0.9),
            new RetrievedChunk(TestData.Chunk(1, "Then run migrations.", "Upgrade"), 0.8),
        ]);
        _chat.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Use pg_restore [1], then migrate [2].")));

        var answer = await CreateService().AskAsync("How do I restore?", [], TestContext.Current.CancellationToken);

        Assert.Equal("Use pg_restore [1], then migrate [2].", answer.Answer);
        Assert.Equal([(1, "Database"), (2, "Upgrade")], answer.Citations.Select(c => (c.Number, c.HeadingPath)));
    }

    [Fact]
    public async Task AskStreamingAsync_StreamsModelTextWithCitations()
    {
        _index.SearchAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new RetrievedChunk(TestData.Chunk(0, "Restore with pg_restore.", "Database"), 0.9)]);
        _chat.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponseUpdate[] { new(ChatRole.Assistant, "Use pg_restore"), new(null, " [1].") }.ToAsyncEnumerable());

        var answer = await CreateService().AskStreamingAsync("How do I restore?", [], TestContext.Current.CancellationToken);

        Assert.Equal(
            [new AnswerPart(AnswerPartKind.Answer, "Use pg_restore"), new AnswerPart(AnswerPartKind.Answer, " [1].")],
            await answer.Parts.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Database", Assert.Single(answer.Citations).HeadingPath);
    }

    [Fact]
    public async Task AskStreamingAsync_SeparatesReasoningFromAnswer()
    {
        _index.SearchAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new RetrievedChunk(TestData.Chunk(0, "Restore with pg_restore.", "Database"), 0.9)]);
        _chat.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponseUpdate[]
            {
                new(ChatRole.Assistant, [new TextReasoningContent("Excerpt 1 covers restores.")]),
                new(null, [new TextReasoningContent(""), new TextContent("Use pg_restore [1].")]),
            }.ToAsyncEnumerable());

        var answer = await CreateService().AskStreamingAsync("How do I restore?", [], TestContext.Current.CancellationToken);

        Assert.Equal(
            [new AnswerPart(AnswerPartKind.Reasoning, "Excerpt 1 covers restores."), new AnswerPart(AnswerPartKind.Answer, "Use pg_restore [1].")],
            await answer.Parts.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AskStreamingAsync_WithoutRelevantChunks_DoesNotCallModel()
    {
        _index.SearchAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new RetrievedChunk(TestData.Chunk(0, "irrelevant"), 0.1)]);

        var answer = await CreateService().AskStreamingAsync("¿Qué es esto?", [], TestContext.Current.CancellationToken);

        Assert.Equal(
            [new AnswerPart(AnswerPartKind.Answer, AnswerService.NoResultsAnswer)],
            await answer.Parts.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(answer.Citations);
        _chat.DidNotReceive().GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void BuildUserPrompt_WrapsExcerptsAsNumberedData()
    {
        var prompt = AnswerService.BuildUserPrompt("Q?", [new RetrievedChunk(TestData.Chunk(0, "Body", "Sec"), 0.9)]);

        Assert.Contains("<excerpt number=\"1\" title=\"Runbook\" section=\"Sec\">", prompt, StringComparison.Ordinal);
        Assert.EndsWith("Question: Q?" + Environment.NewLine, prompt, StringComparison.Ordinal);
    }
}
