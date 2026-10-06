namespace Outline.Rag.Api.Security;

/// <summary>
/// How callers prove who they are. Every caller ends up identified by an email address, which is matched to an
/// Outline user to decide what they may search. With neither option configured, every request is rejected.
/// </summary>
public sealed class RagAuthOptions
{
    public const string SectionName = "Auth";

    public OidcAuthOptions Oidc { get; set; } = new();

    public GatewayAuthOptions Gateway { get; set; } = new();
}

/// <summary>
/// JWT bearer tokens from an OpenID Connect provider, normally the one Outline itself signs in with.
/// Enabled when <see cref="Authority"/> is set.
/// </summary>
public sealed class OidcAuthOptions
{
    /// <summary>Issuer URL, e.g. https://login.example.com/realms/corp. Its discovery document supplies the keys.</summary>
    public string? Authority { get; set; }

    /// <summary>Expected <c>aud</c> claim: the client id or API identifier registered for this service.</summary>
    public string? Audience { get; set; }

    /// <summary>Claim holding the user's email, as Outline knows it (some providers use preferred_username or upn).</summary>
    public string EmailClaim { get; set; } = "email";
}

/// <summary>
/// A trusted front end, such as Open WebUI, that signs users in itself and forwards who they are. It sends
/// <c>Authorization: Bearer &lt;ApiKey&gt;</c> plus the user's email in <see cref="UserEmailHeader"/>; anyone holding
/// the key can claim to be any user, so treat it like a password and only give it to that front end.
/// Enabled when <see cref="ApiKey"/> is set.
/// </summary>
public sealed class GatewayAuthOptions
{
    public string? ApiKey { get; set; }

    /// <summary>Open WebUI sends this when ENABLE_FORWARD_USER_INFO_HEADERS is true.</summary>
    public string UserEmailHeader { get; set; } = "X-OpenWebUI-User-Email";
}
