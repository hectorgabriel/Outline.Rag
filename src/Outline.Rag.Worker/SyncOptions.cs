using System.ComponentModel.DataAnnotations;

namespace Outline.Rag.Worker;

public sealed class SyncOptions
{
    public const string SectionName = "Sync";

    [Range(typeof(TimeSpan), "00:00:30", "1.00:00:00")]
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(15);
}
