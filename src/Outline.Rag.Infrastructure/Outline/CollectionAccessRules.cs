namespace Outline.Rag.Infrastructure.Outline;

/// <summary>
/// Outline's read rule for collections, applied to a snapshot of users, collections and memberships:
/// a user can read a collection when it has a default permission ("read"/"read_write") and the user is not a guest,
/// or when the user is a member of it directly or through a group. Suspended users can read nothing.
/// </summary>
/// <remarks>
/// Document-level sharing (a single document in a private collection shared with someone) is not modelled, so
/// such documents stay out of that person's results: the rule errs towards showing less.
/// </remarks>
internal static class CollectionAccessRules
{
    public const string GuestRole = "guest";

    /// <param name="collectionMembers">Collection id → ids of users added to it directly.</param>
    /// <param name="collectionGroups">Collection id → ids of groups added to it.</param>
    /// <param name="groupMembers">Group id → ids of its users.</param>
    /// <returns>Email (case-insensitive) → readable collection ids, for every active user.</returns>
    public static Dictionary<string, IReadOnlySet<Guid>> Build(
        IEnumerable<OutlineUserDto> users,
        IEnumerable<OutlineCollectionDto> collections,
        IReadOnlyDictionary<Guid, IReadOnlyCollection<Guid>> collectionMembers,
        IReadOnlyDictionary<Guid, IReadOnlyCollection<Guid>> collectionGroups,
        IReadOnlyDictionary<Guid, IReadOnlyCollection<Guid>> groupMembers)
    {
        var openToTeam = new HashSet<Guid>();
        var explicitReaders = new Dictionary<Guid, HashSet<Guid>>(); // collection id → user ids
        foreach (var collection in collections)
        {
            if (collection.Permission is not null)
            {
                openToTeam.Add(collection.Id);
            }

            var readers = new HashSet<Guid>(collectionMembers.GetValueOrDefault(collection.Id) ?? []);
            foreach (var groupId in collectionGroups.GetValueOrDefault(collection.Id) ?? [])
            {
                readers.UnionWith(groupMembers.GetValueOrDefault(groupId) ?? []);
            }

            explicitReaders[collection.Id] = readers;
        }

        var result = new Dictionary<string, IReadOnlySet<Guid>>(StringComparer.OrdinalIgnoreCase);
        foreach (var user in users)
        {
            if (user.IsSuspended)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(user.Email))
            {
                throw new InvalidOperationException(
                    "Outline returned users without email addresses, so callers can't be matched to Outline users. "
                    + "Outline only shows emails to admins: Outline:ApiToken must belong to an admin account.");
            }

            var isGuest = string.Equals(user.Role, GuestRole, StringComparison.OrdinalIgnoreCase);
            var readable = explicitReaders
                .Where(c => (!isGuest && openToTeam.Contains(c.Key)) || c.Value.Contains(user.Id))
                .Select(c => c.Key)
                .ToHashSet();

            var email = user.Email.Trim();
            if (result.TryGetValue(email, out var existing))
            {
                readable.UnionWith(existing);
            }

            result[email] = readable;
        }

        return result;
    }
}
