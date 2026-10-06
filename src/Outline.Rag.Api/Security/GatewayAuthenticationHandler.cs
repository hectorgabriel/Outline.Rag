using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Outline.Rag.Api.Security;

internal sealed class GatewayAuthenticationOptions : AuthenticationSchemeOptions
{
    public string? ApiKey { get; set; }

    public string UserEmailHeader { get; set; } = "X-OpenWebUI-User-Email";
}

/// <summary>
/// Authenticates requests from a trusted front end (see <see cref="GatewayAuthOptions"/>): a matching key in the
/// Authorization header, plus the signed-in user's email in a header the front end sets.
/// </summary>
internal sealed class GatewayAuthenticationHandler(
    IOptionsMonitor<GatewayAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<GatewayAuthenticationOptions>(options, logger, encoder)
{
    public const string SchemeName = "Gateway";

    private const string BearerPrefix = "Bearer ";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (string.IsNullOrEmpty(Options.ApiKey))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!KeyMatches(authorization[BearerPrefix.Length..].Trim(), Options.ApiKey))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid gateway key."));
        }

        var email = Request.Headers[Options.UserEmailHeader].ToString().Trim();
        if (email.Length == 0)
        {
            return Task.FromResult(AuthenticateResult.Fail($"The {Options.UserEmailHeader} header is missing."));
        }

        var identity = new ClaimsIdentity([new Claim(RagClaims.Email, email)], Scheme.Name, RagClaims.Email, roleType: null);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }

    /// <summary>Constant-time comparison; hashing first also hides the key's length.</summary>
    internal static bool KeyMatches(string presented, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(presented)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}
