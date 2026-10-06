using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Outline.Rag.Application.Abstractions;

namespace Outline.Rag.Infrastructure.Outline;

/// <summary>
/// Resolves readable collections from Outline's own users, collections and memberships, read through the API with
/// the admin <see cref="OutlineOptions.ApiToken"/>. The whole team's access is rebuilt at once and reused for
/// <see cref="OutlineOptions.AccessCacheDuration"/>, so a search costs no Outline calls on a warm cache.
/// </summary>
/// <remarks>
/// If Outline can't be read, the refresh fails and so does the request: an unknown permission is never treated as
/// access.
/// </remarks>
internal sealed partial class OutlineCollectionAccessResolver(
    IHttpClientFactory httpClientFactory,
    IOptions<OutlineOptions> options,
    TimeProvider timeProvider,
    ILogger<OutlineCollectionAccessResolver> logger) : ICollectionAccessResolver, IDisposable
{
    public const string HttpClientName = "OutlineAccess";

    private static readonly IReadOnlySet<Guid> NoCollections = new HashSet<Guid>();

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private Snapshot? _snapshot;

    public async Task<IReadOnlySet<Guid>> GetReadableCollectionIdsAsync(string email, CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return snapshot.ReadableByEmail.GetValueOrDefault(email.Trim()) ?? NoCollections;
    }

    public void Dispose() => _refreshLock.Dispose();

    private async Task<Snapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _snapshot) is { } fresh && timeProvider.GetUtcNow() < fresh.ExpiresAt)
        {
            return fresh;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Another request may have refreshed it while this one waited.
            if (_snapshot is { } refreshed && timeProvider.GetUtcNow() < refreshed.ExpiresAt)
            {
                return refreshed;
            }

            var snapshot = await BuildAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _snapshot, snapshot);
            return snapshot;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<Snapshot> BuildAsync(CancellationToken cancellationToken)
    {
        var http = httpClientFactory.CreateClient(HttpClientName);

        var users = await ListAllAsync<OutlineUserDto, List<OutlineUserDto>>(
            http, "api/users.list", (offset, limit) => new OutlineUsersListRequest(offset, limit), d => d, cancellationToken)
            .ConfigureAwait(false);
        var collections = await ListAllAsync<OutlineCollectionDto, List<OutlineCollectionDto>>(
            http, "api/collections.list", (offset, limit) => new OutlinePageRequest(offset, limit), d => d, cancellationToken)
            .ConfigureAwait(false);

        var collectionMembers = new Dictionary<Guid, IReadOnlyCollection<Guid>>();
        var collectionGroups = new Dictionary<Guid, IReadOnlyCollection<Guid>>();
        foreach (var collection in collections)
        {
            var members = await ListAllAsync<OutlineUserMembershipDto, OutlineUserMembershipList>(
                http, "api/collections.memberships", (offset, limit) => new OutlineIdPageRequest(collection.Id, offset, limit),
                d => d.Memberships, cancellationToken).ConfigureAwait(false);
            collectionMembers[collection.Id] = [.. members.Select(m => m.UserId)];

            var groups = await ListAllAsync<OutlineCollectionGroupDto, OutlineCollectionGroupList>(
                http, "api/collections.group_memberships", (offset, limit) => new OutlineIdPageRequest(collection.Id, offset, limit),
                d => d.GroupMemberships, cancellationToken).ConfigureAwait(false);
            collectionGroups[collection.Id] = [.. groups.Select(g => g.GroupId)];
        }

        // Only groups that grant access to some collection matter.
        var groupMembers = new Dictionary<Guid, IReadOnlyCollection<Guid>>();
        foreach (var groupId in collectionGroups.Values.SelectMany(g => g).Distinct())
        {
            var members = await ListAllAsync<OutlineUserMembershipDto, OutlineGroupUserList>(
                http, "api/groups.memberships", (offset, limit) => new OutlineIdPageRequest(groupId, offset, limit),
                d => d.GroupMemberships, cancellationToken).ConfigureAwait(false);
            groupMembers[groupId] = [.. members.Select(m => m.UserId)];
        }

        var readable = CollectionAccessRules.Build(users, collections, collectionMembers, collectionGroups, groupMembers);
        LogRefreshed(logger, readable.Count, collections.Count, groupMembers.Count);
        return new Snapshot(readable, timeProvider.GetUtcNow() + options.Value.AccessCacheDuration);
    }

    /// <summary>
    /// Reads every page of a list method. A short read only ever loses access (a missing user, membership or
    /// collection), never grants it, so unlike the index reconciliation this doesn't check the reported total.
    /// </summary>
    private async Task<List<TItem>> ListAllAsync<TItem, TData>(
        HttpClient http,
        string path,
        Func<int, int, object> request,
        Func<TData, IReadOnlyList<TItem>> items,
        CancellationToken cancellationToken)
    {
        var pageSize = options.Value.PageSize;
        var all = new List<TItem>();
        for (var offset = 0; ; offset += pageSize)
        {
            var page = await http.PostOutlineAsync<TData>(path, request(offset, pageSize), cancellationToken).ConfigureAwait(false);
            var pageItems = items(page.Data);
            all.AddRange(pageItems);
            if (pageItems.Count < pageSize)
            {
                return all;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Refreshed Outline permissions: {UserCount} active users, {CollectionCount} collections, {GroupCount} groups")]
    private static partial void LogRefreshed(ILogger logger, int userCount, int collectionCount, int groupCount);

    private sealed record Snapshot(IReadOnlyDictionary<string, IReadOnlySet<Guid>> ReadableByEmail, DateTimeOffset ExpiresAt);
}
