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

    /// <summary>
    /// Time allowed for one chat call. Unset means the provider default: the Anthropic SDK's own, and
    /// <see cref="DefaultOllamaTimeout"/> for Ollama, whose HttpClient would otherwise give up after 100 s.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:10", "01:00:00")]
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Ollama only: let thinking models (qwen3.5, deepseek-r1, ...) reason before answering. Off by default because
    /// local reasoning takes minutes; when on, the reasoning streams to chat UIs. Anthropic always thinks adaptively.
    /// </summary>
    public bool Thinking { get; set; }

    /// <summary>Local models on CPU/Metal write ~20-30 tokens/s, so a long grounded answer needs minutes.</summary>
    public static readonly TimeSpan DefaultOllamaTimeout = TimeSpan.FromMinutes(5);
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
