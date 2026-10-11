using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Outline.Rag.Application.Abstractions;

namespace Outline.Rag.Infrastructure.AI;

/// <summary>
/// <see cref="IReranker"/> over llama.cpp's <c>llama-server --rerank</c>, serving a cross-encoder such as
/// bge-reranker-v2-m3 (multilingual, the reranking companion of the bge-m3 embeddings).
/// </summary>
internal sealed class LlamaCppReranker(HttpClient http, IOptions<AiOptions> options) : IReranker
{
    public async Task<IReadOnlyList<double>> ScoreAsync(
        string query,
        IReadOnlyList<string> passages,
        CancellationToken cancellationToken)
    {
        if (passages.Count == 0)
        {
            return [];
        }

        using var response = await http
            .PostAsJsonAsync("v1/rerank", new RerankRequest(options.Value.Reranker.Model, query, passages), cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<RerankResponse>(cancellationToken).ConfigureAwait(false);
        var scores = new double[passages.Count];
        var seen = 0;
        foreach (var result in body?.Results ?? [])
        {
            if (result.Index >= 0 && result.Index < scores.Length)
            {
                scores[result.Index] = Sigmoid(result.RelevanceScore);
                seen++;
            }
        }

        return seen == passages.Count
            ? scores
            : throw new InvalidOperationException($"The reranker scored {seen} of {passages.Count} passages.");
    }

    /// <summary>llama-server returns the model's raw logit; the sigmoid maps it to 0-1 like the reference implementation.</summary>
    internal static double Sigmoid(double logit) => 1 / (1 + Math.Exp(-logit));

    private sealed record RerankRequest(string Model, string Query, IReadOnlyList<string> Documents)
    {
        [JsonPropertyName("top_n")]
        public int TopN => Documents.Count;
    }

    private sealed record RerankResponse(IReadOnlyList<RerankResult>? Results);

    private sealed record RerankResult(int Index, [property: JsonPropertyName("relevance_score")] double RelevanceScore);
}
