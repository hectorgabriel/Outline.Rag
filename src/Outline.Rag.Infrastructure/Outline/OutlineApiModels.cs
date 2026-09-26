using System.Text.Json.Serialization;

namespace Outline.Rag.Infrastructure.Outline;

// Subset of the Outline API schema (https://www.getoutline.com/developers). Outline's API is RPC-style:
// every call is a POST to /api/<resource>.<method> with a JSON body.

internal sealed record OutlineListRequest(
    [property: JsonPropertyName("offset")] int Offset,
    [property: JsonPropertyName("limit")] int Limit,
    [property: JsonPropertyName("sort")] string Sort = "updatedAt",
    [property: JsonPropertyName("direction")] string Direction = "DESC");

internal sealed record OutlineInfoRequest([property: JsonPropertyName("id")] Guid Id);

internal sealed record OutlineResponse<T>([property: JsonPropertyName("data")] T Data);

internal sealed record OutlineDocumentDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("collectionId")] Guid? CollectionId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("publishedAt")] DateTimeOffset? PublishedAt,
    [property: JsonPropertyName("archivedAt")] DateTimeOffset? ArchivedAt,
    [property: JsonPropertyName("deletedAt")] DateTimeOffset? DeletedAt);

/// <summary>Envelope of an Outline webhook delivery.</summary>
public sealed record OutlineWebhookEvent(
    [property: JsonPropertyName("event")] string Event,
    [property: JsonPropertyName("payload")] OutlineWebhookPayload Payload);

public sealed record OutlineWebhookPayload([property: JsonPropertyName("id")] Guid Id);
