using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace Outline.Rag.Api.Security;

internal static partial class SecurityServiceCollectionExtensions
{
    private const string SelectorScheme = "Rag";

    /// <summary>
    /// Registers authentication (OIDC JWTs and/or a trusted gateway, see <see cref="RagAuthOptions"/>) and makes
    /// "signed in, with an email" the default for every endpoint. Endpoints that must stay open opt out with
    /// AllowAnonymous, so a newly added endpoint is protected unless someone decides otherwise.
    /// </summary>
    public static IServiceCollection AddRagSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        var auth = configuration.GetSection(RagAuthOptions.SectionName).Get<RagAuthOptions>() ?? new RagAuthOptions();
        var oidcEnabled = !string.IsNullOrWhiteSpace(auth.Oidc.Authority);
        var gatewayEnabled = !string.IsNullOrWhiteSpace(auth.Gateway.ApiKey);

        var builder = services.AddAuthentication(SelectorScheme)
            .AddPolicyScheme(SelectorScheme, "JWT or gateway", policy => policy.ForwardDefaultSelector = context =>
                // Only a request carrying the gateway's user header is a gateway request; everything else is a JWT.
                // With JWTs disabled, the gateway handler answers instead, rejecting anything without its key.
                (gatewayEnabled && context.Request.Headers.ContainsKey(auth.Gateway.UserEmailHeader)) || !oidcEnabled
                    ? GatewayAuthenticationHandler.SchemeName
                    : JwtBearerDefaults.AuthenticationScheme)
            .AddScheme<GatewayAuthenticationOptions, GatewayAuthenticationHandler>(GatewayAuthenticationHandler.SchemeName, o =>
            {
                o.ApiKey = auth.Gateway.ApiKey;
                o.UserEmailHeader = auth.Gateway.UserEmailHeader;
            });

        if (oidcEnabled)
        {
            builder.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                o.Authority = auth.Oidc.Authority;
                o.Audience = auth.Oidc.Audience;
                o.MapInboundClaims = false;
                o.TokenValidationParameters.ValidateAudience = !string.IsNullOrWhiteSpace(auth.Oidc.Audience);
                o.Events = new JwtBearerEvents
                {
                    // Normalise the provider's email claim to the one the rest of the Api reads.
                    OnTokenValidated = context =>
                    {
                        if (context.Principal?.Identity is ClaimsIdentity identity
                            && !identity.HasClaim(c => c.Type == RagClaims.Email)
                            && identity.FindFirst(auth.Oidc.EmailClaim)?.Value is { Length: > 0 } email)
                        {
                            identity.AddClaim(new Claim(RagClaims.Email, email));
                        }

                        return Task.CompletedTask;
                    },
                };
            });
        }

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireClaim(RagClaims.Email)
                .Build());

        services.AddSingleton<CallerCollections>();
        return services;
    }

    public static void WarnIfNoAuthentication(this WebApplication app)
    {
        var auth = app.Configuration.GetSection(RagAuthOptions.SectionName).Get<RagAuthOptions>();
        if (string.IsNullOrWhiteSpace(auth?.Oidc.Authority) && string.IsNullOrWhiteSpace(auth?.Gateway.ApiKey))
        {
            LogNoAuthentication(app.Logger);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No authentication is configured (Auth:Oidc:Authority, Auth:Gateway:ApiKey): every search and chat request will get 401")]
    private static partial void LogNoAuthentication(ILogger logger);
}
