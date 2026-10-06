using System.Text.Json;
using Microsoft.Extensions.Options;
using Outline.Rag.Application.Ingestion;
using Outline.Rag.Infrastructure.Outline;

namespace Outline.Rag.Api.Endpoints;

internal static class OutlineWebhookEndpoints
{
    /// <summary>
    /// Receives Outline webhook deliveries (Settings → Webhooks, subscribe to document events) and queues the
    /// affected document for re-indexing. Outline expects a fast 2xx, so work happens in the background.
    /// </summary>
    public static IEndpointRouteBuilder MapOutlineWebhook(this IEndpointRouteBuilder app)
    {
        app.MapPost("/webhooks/outline", async (HttpRequest request, IOptions<OutlineOptions> options, ReindexChannel queue) =>
            {
                using var reader = new StreamReader(request.Body);
                var body = await reader.ReadToEndAsync(request.HttpContext.RequestAborted);

                var secret = options.Value.WebhookSigningSecret;
                if (string.IsNullOrEmpty(secret)
                    || !OutlineWebhookSignature.IsValid(request.Headers[OutlineWebhookSignature.HeaderName], body, secret))
                {
                    return Results.Unauthorized();
                }

                var evt = JsonSerializer.Deserialize<OutlineWebhookEvent>(body);
                if (evt?.Event.StartsWith("documents.", StringComparison.Ordinal) == true)
                {
                    // Every document event (update, publish, archive, delete, move, ...) re-reads the document:
                    // OutlineSyncService.SyncDocumentAsync removes it from the index if it is no longer readable.
                    queue.Enqueue(evt.Payload.Id);
                }

                return Results.Accepted();
            })
            .WithTags("Webhooks")
            .ExcludeFromDescription()
            .AllowAnonymous(); // Authenticated by its HMAC signature instead.

        return app;
    }
}
