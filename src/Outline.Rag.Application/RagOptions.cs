using System.ComponentModel.DataAnnotations;

namespace Outline.Rag.Application;

public sealed class RagOptions
{
    public const string SectionName = "Rag";

    /// <summary>Number of chunks retrieved per question.</summary>
    [Range(1, 50)]
    public int TopK { get; set; } = 6;

    /// <summary>
    /// Chunks scoring below this cosine similarity are discarded. Tuned for bge-m3 with tools/Outline.Rag.Eval:
    /// 0.3 kept everything, even off-topic questions; above 0.5 real answers start to drop out.
    /// </summary>
    [Range(0.0, 1.0)]
    public double MinScore { get; set; } = 0.5;

    /// <summary>
    /// With a reranker configured: how many vector-search candidates it reorders before the best
    /// <see cref="TopK"/> are kept. More finds answers ranked lower by embeddings, at the cost of reranking time.
    /// </summary>
    [Range(1, 200)]
    public int RerankCandidates { get; set; } = 30;

    /// <summary>
    /// With a reranker configured, this replaces <see cref="MinScore"/>: candidates whose reranker score (0-1) is
    /// below it are discarded. The two are on different scales, so each is tuned separately with tools/Outline.Rag.Eval.
    /// </summary>
    [Range(0.0, 1.0)]
    public double RerankMinScore { get; set; } = 0.1;

    [Range(64, 8192)]
    public int MaxChunkTokens { get; set; } = 512;

    [Range(0, 1024)]
    public int ChunkOverlapTokens { get; set; } = 64;

    /// <summary>Documents embedded per embedding-generator request.</summary>
    [Range(1, 512)]
    public int EmbeddingBatchSize { get; set; } = 32;
}
