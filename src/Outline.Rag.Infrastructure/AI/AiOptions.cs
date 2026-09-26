using System.ComponentModel.DataAnnotations;

namespace Outline.Rag.Infrastructure.AI;

public sealed class AiOptions
{
    public const string SectionName = "AI";

    [Required]
    public ChatModelOptions Chat { get; set; } = new();

    [Required]
    public EmbeddingModelOptions Embeddings { get; set; } = new();
}

public enum ChatProvider
{
    Anthropic,
    Ollama,
}

public enum EmbeddingProvider
{
    Ollama,
}

public sealed class ChatModelOptions
{
    public ChatProvider Provider { get; set; } = ChatProvider.Anthropic;

    [Required]
    public string Model { get; set; } = "claude-opus-5";

    /// <summary>Ollama endpoint. Ignored for Anthropic (use ANTHROPIC_BASE_URL to override its base URL).</summary>
    public Uri? Endpoint { get; set; }

    /// <summary>Anthropic API key. When empty the SDK falls back to ANTHROPIC_API_KEY / `ant auth login`.</summary>
    public string? ApiKey { get; set; }

    [Range(256, 128_000)]
    public int MaxOutputTokens { get; set; } = 16_000;
}

public sealed class EmbeddingModelOptions
{
    public EmbeddingProvider Provider { get; set; } = EmbeddingProvider.Ollama;

    /// <summary>bge-m3 is multilingual, which matters for Spanish-language wiki content.</summary>
    [Required]
    public string Model { get; set; } = "bge-m3";

    public Uri Endpoint { get; set; } = new("http://localhost:11434");

    /// <summary>Must match the model's output size (bge-m3: 1024). Fixed per vector table.</summary>
    [Range(1, 16_000)]
    public int Dimensions { get; set; } = 1024;
}
