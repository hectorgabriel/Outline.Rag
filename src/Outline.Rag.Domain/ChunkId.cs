using System.Security.Cryptography;
using System.Text;

namespace Outline.Rag.Domain;

public static class ChunkId
{
    /// <summary>
    /// Stable, name-based GUID for chunk <paramref name="index"/> of <paramref name="documentId"/>.
    /// </summary>
    public static Guid For(Guid documentId, int index)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes($"{documentId:N}:{index}"), hash);
        return new Guid(hash[..16]);
    }
}
