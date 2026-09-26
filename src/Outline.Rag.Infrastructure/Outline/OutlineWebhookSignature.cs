using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Outline.Rag.Infrastructure.Outline;

/// <summary>
/// Verifies the <c>Outline-Signature</c> header (<c>t=&lt;unix ms&gt;,s=&lt;hex HMAC-SHA256&gt;</c>), where the
/// HMAC is computed with the subscription's signing secret over <c>"{t}.{rawBody}"</c>.
/// </summary>
public static class OutlineWebhookSignature
{
    public const string HeaderName = "Outline-Signature";

    public static bool IsValid(string? header, string rawBody, string secret)
    {
        if (string.IsNullOrEmpty(header) || string.IsNullOrEmpty(secret))
        {
            return false;
        }

        string? timestamp = null, signature = null;
        foreach (var part in header.Split(','))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2)
            {
                continue;
            }

            switch (kv[0].Trim())
            {
                case "t": timestamp = kv[1].Trim(); break;
                case "s": signature = kv[1].Trim(); break;
            }
        }

        if (timestamp is null || signature is null)
        {
            return false;
        }

        var expected = Compute(timestamp, rawBody, secret);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(signature.ToLowerInvariant()));
    }

    internal static string Compute(string timestamp, string rawBody, string secret)
    {
        var mac = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{timestamp}.{rawBody}")));
        return Convert.ToHexStringLower(mac);
    }
}
