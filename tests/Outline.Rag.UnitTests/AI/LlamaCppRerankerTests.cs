using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Outline.Rag.Infrastructure.AI;

namespace Outline.Rag.UnitTests.AI;

public sealed class LlamaCppRerankerTests
{
    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private static LlamaCppReranker Reranker(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://reranker.test/") }, Options.Create(new AiOptions()));

    [Fact]
    public async Task ScoreAsync_MapsResultsBackToPassageOrderAsProbabilities()
    {
        // llama-server sorts results by score and returns raw logits.
        var handler = new StubHandler("""{"results":[{"index":1,"relevance_score":5.0},{"index":0,"relevance_score":-5.0}]}""");

        var scores = await Reranker(handler).ScoreAsync("q", ["off topic", "answer"], TestContext.Current.CancellationToken);

        Assert.Equal(LlamaCppReranker.Sigmoid(-5), scores[0], 6);
        Assert.Equal(LlamaCppReranker.Sigmoid(5), scores[1], 6);
        Assert.True(scores[1] > 0.99 && scores[0] < 0.01);
        Assert.Contains("\"documents\":[\"off topic\",\"answer\"]", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("\"top_n\":2", handler.RequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScoreAsync_WithMissingResults_Throws()
    {
        var handler = new StubHandler("""{"results":[{"index":0,"relevance_score":1.0}]}""");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Reranker(handler).ScoreAsync("q", ["a", "b"], TestContext.Current.CancellationToken));
    }
}
