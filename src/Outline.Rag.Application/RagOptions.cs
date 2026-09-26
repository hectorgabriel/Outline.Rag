using System.ComponentModel.DataAnnotations;

namespace Outline.Rag.Application;

public sealed class RagOptions
{
    public const string SectionName = "Rag";

    /// <summary>Number of chunks retrieved per question.</summary>
    [Range(1, 50)]
    public int TopK { get; set; } = 6;

    /// <summary>Chunks scoring below this cosine similarity are discarded.</summary>
    [Range(0.0, 1.0)]
    public double MinScore { get; set; } = 0.3;

    [Range(64, 8192)]
    public int MaxChunkTokens { get; set; } = 512;

    [Range(0, 1024)]
    public int ChunkOverlapTokens { get; set; } = 64;

    /// <summary>Documents embedded per embedding-generator request.</summary>
    [Range(1, 512)]
    public int EmbeddingBatchSize { get; set; } = 32;
}
