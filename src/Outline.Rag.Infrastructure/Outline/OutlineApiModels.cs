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

internal sealed record OutlinePageRequest(
    [property: JsonPropertyName("offset")] int Offset,
    [property: JsonPropertyName("limit")] int Limit);

internal sealed record OutlineResponse<T>(
    [property: JsonPropertyName("data")] T Data,
    [property: JsonPropertyName("pagination")] OutlinePagination? Pagination = null);

internal sealed record OutlinePagination([property: JsonPropertyName("total")] int? Total);

/// <param name="Permission">
/// The access every non-guest team member gets ("read" or "read_write"); <see langword="null"/> for a private
/// collection, which only its members and member groups can open.
/// </param>
internal sealed record OutlineCollectionDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("permission")] string? Permission = null);

internal sealed record OutlineIdPageRequest(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("offset")] int Offset,
    [property: JsonPropertyName("limit")] int Limit);

/// <param name="Filter">"all" includes invited users who haven't signed in yet; suspended ones are dropped later.</param>
internal sealed record OutlineUsersListRequest(
    [property: JsonPropertyName("offset")] int Offset,
    [property: JsonPropertyName("limit")] int Limit,
    [property: JsonPropertyName("filter")] string Filter = "all");

/// <param name="Email">Only returned to admins (and to the user themselves), hence nullable.</param>
internal sealed record OutlineUserDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("isSuspended")] bool IsSuspended);

internal sealed record OutlineGroupDto([property: JsonPropertyName("id")] Guid Id);

internal sealed record OutlineGroupList([property: JsonPropertyName("groups")] IReadOnlyList<OutlineGroupDto> Groups);

/// <summary><c>collections.memberships</c>: users added to a collection directly.</summary>
internal sealed record OutlineUserMembershipList(
    [property: JsonPropertyName("memberships")] IReadOnlyList<OutlineUserMembershipDto> Memberships);

internal sealed record OutlineUserMembershipDto([property: JsonPropertyName("userId")] Guid UserId);

/// <summary><c>collections.group_memberships</c>: groups added to a collection.</summary>
internal sealed record OutlineCollectionGroupList(
    [property: JsonPropertyName("groupMemberships")] IReadOnlyList<OutlineCollectionGroupDto> GroupMemberships);

internal sealed record OutlineCollectionGroupDto([property: JsonPropertyName("groupId")] Guid GroupId);

/// <summary><c>groups.memberships</c>: the users in a group.</summary>
internal sealed record OutlineGroupUserList(
    [property: JsonPropertyName("groupMemberships")] IReadOnlyList<OutlineUserMembershipDto> GroupMemberships);

/// <summary>A node of <c>collections.documents</c>: the collection's tree of published documents.</summary>
internal sealed record OutlineNavigationNode(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("children")] IReadOnlyList<OutlineNavigationNode>? Children);

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
