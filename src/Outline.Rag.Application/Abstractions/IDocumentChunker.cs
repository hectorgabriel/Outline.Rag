using Outline.Rag.Domain;

namespace Outline.Rag.Application.Abstractions;

public interface IDocumentChunker
{
    IReadOnlyList<DocumentChunk> Chunk(SourceDocument document);
}
