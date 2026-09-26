using System.ComponentModel.DataAnnotations;

namespace Outline.Rag.Infrastructure.Outline;

public sealed class OutlineOptions
{
    public const string SectionName = "Outline";

    /// <summary>Public URL of the Outline instance, e.g. https://wiki.example.com/.</summary>
    [Required]
    public Uri? BaseUrl { get; set; }

    /// <summary>
    /// Outline API key (Settings → API). Every indexed document is visible to this key's user, so use a
    /// dedicated read-only account with access to exactly the collections the RAG should serve.
    /// </summary>
    [Required]
    public string ApiToken { get; set; } = "";

    [Range(1, 100)]
    public int PageSize { get; set; } = 100;

    /// <summary>Signing secret of the Outline webhook subscription; webhooks are rejected when empty.</summary>
    public string? WebhookSigningSecret { get; set; }
}
