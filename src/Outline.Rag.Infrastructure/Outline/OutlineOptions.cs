using System.ComponentModel.DataAnnotations;

namespace Outline.Rag.Infrastructure.Outline;

public sealed class OutlineOptions
{
    public const string SectionName = "Outline";

    /// <summary>Public URL of the Outline instance, e.g. https://wiki.example.com/.</summary>
    [Required]
    public Uri? BaseUrl { get; set; }

    /// <summary>
    /// Outline API key (Settings → API). The RAG indexes the collections this key's user can read, and maps
    /// callers to Outline users by email, which Outline only shows to admins: use a dedicated admin account.
    /// </summary>
    [Required]
    public string ApiToken { get; set; } = "";

    [Range(1, 100)]
    public int PageSize { get; set; } = 100;

    /// <summary>
    /// How long the snapshot of who can read which collection is reused before Outline is asked again. Permission
    /// changes in Outline take up to this long to reach search.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:10", "01:00:00")]
    public TimeSpan AccessCacheDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Signing secret of the Outline webhook subscription; webhooks are rejected when empty.</summary>
    public string? WebhookSigningSecret { get; set; }
}
