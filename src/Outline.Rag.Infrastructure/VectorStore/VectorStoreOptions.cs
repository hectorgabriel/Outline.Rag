using System.ComponentModel.DataAnnotations;

namespace Outline.Rag.Infrastructure.VectorStore;

public sealed class VectorStoreOptions
{
    public const string SectionName = "VectorStore";

    /// <summary>Table holding the chunks. Changing the embedding model or dimension requires a new table.</summary>
    [Required]
    public string CollectionName { get; set; } = "outline_chunks";
}
