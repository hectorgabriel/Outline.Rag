using System.Security.Claims;
using Outline.Rag.Application.Abstractions;

namespace Outline.Rag.Api.Security;

internal static class RagClaims
{
    /// <summary>Every authentication scheme puts the caller's email in this claim.</summary>
    public const string Email = "email";
}

/// <summary>
/// Turns the authenticated caller into the collections a search may touch: the ones they can read in Outline,
/// narrowed to <c>requested</c> when the request names some. Collections they can't read are dropped silently.
/// </summary>
internal sealed class CallerCollections(ICollectionAccessResolver resolver)
{
    public async Task<IReadOnlyCollection<Guid>> ResolveAsync(
        ClaimsPrincipal caller, IReadOnlyCollection<Guid>? requested, CancellationToken cancellationToken)
    {
        var email = caller.FindFirstValue(RagClaims.Email);
        if (string.IsNullOrWhiteSpace(email))
        {
            return [];
        }

        var readable = await resolver.GetReadableCollectionIdsAsync(email, cancellationToken);
        return requested is { Count: > 0 } ? [.. requested.Where(readable.Contains).Distinct()] : [.. readable];
    }
}
