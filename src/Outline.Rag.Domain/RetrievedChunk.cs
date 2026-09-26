namespace Outline.Rag.Domain;

public sealed record RetrievedChunk(DocumentChunk Chunk, double Score);
