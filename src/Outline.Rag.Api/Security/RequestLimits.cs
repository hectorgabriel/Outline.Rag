using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Outline.Rag.Api.Security;

/// <summary>Size caps on what one request may ask for. Each question costs an embedding, and answers a model call.</summary>
internal static class RequestLimits
{
    public const int MaxQuestionLength = 2_000;
    public const int MaxTop = 20;
    public const int MaxCollectionIds = 100;

    /// <summary>Applies to every request, including chat histories and Outline webhook deliveries.</summary>
    public const long MaxRequestBodyBytes = 2 * 1024 * 1024;
}

/// <summary>Requests per caller per minute; over the limit the Api answers 429 with Retry-After.</summary>
public sealed class RagRateLimitOptions
{
    public const string SectionName = "RateLimits";

    /// <summary>/api/search: one embedding each.</summary>
    [Range(1, 100_000)]
    public int SearchPerMinute { get; set; } = 60;

    /// <summary>/api/ask and /v1/chat/completions: an embedding plus a model call each.</summary>
    [Range(1, 100_000)]
    public int AnswersPerMinute { get; set; } = 10;
}

internal static class RagRateLimiting
{
    public const string SearchPolicy = "search";
    public const string AnswerPolicy = "answer";

    public static IServiceCollection AddRagRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RagRateLimitOptions>()
            .BindConfiguration(RagRateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                return ValueTask.CompletedTask;
            };
            limiter.AddPolicy(SearchPolicy, context => PerCaller(context, o => o.SearchPerMinute));
            limiter.AddPolicy(AnswerPolicy, context => PerCaller(context, o => o.AnswersPerMinute));
        });

        return services;
    }

    /// <summary>
    /// One fixed window per caller. Rate-limited endpoints all require authentication, so the email is there; the
    /// address fallback only matters if that ever changes.
    /// </summary>
    private static RateLimitPartition<string> PerCaller(HttpContext context, Func<RagRateLimitOptions, int> permits)
    {
        var limits = context.RequestServices.GetRequiredService<IOptions<RagRateLimitOptions>>().Value;
        var caller = context.User.FindFirstValue(RagClaims.Email)?.ToUpperInvariant()
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(caller, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits(limits),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    }
}
