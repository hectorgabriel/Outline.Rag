using Outline.Rag.Infrastructure.Outline;

namespace Outline.Rag.UnitTests.Outline;

public sealed class CollectionAccessRulesTests
{
    private static readonly Guid Handbook = Guid.NewGuid();   // open to the team ("read_write")
    private static readonly Guid HrPrivate = Guid.NewGuid();  // private, shared with the HR group
    private static readonly Guid Finance = Guid.NewGuid();    // private, one direct member
    private static readonly Guid HrGroup = Guid.NewGuid();

    private static readonly OutlineCollectionDto[] Collections =
    [
        new(Handbook, "read_write"),
        new(HrPrivate, null),
        new(Finance, null),
    ];

    private static Dictionary<string, IReadOnlySet<Guid>> Build(
        IEnumerable<OutlineUserDto> users,
        Dictionary<Guid, IReadOnlyCollection<Guid>>? members = null,
        Dictionary<Guid, IReadOnlyCollection<Guid>>? groups = null,
        Dictionary<Guid, IReadOnlyCollection<Guid>>? groupMembers = null) =>
        CollectionAccessRules.Build(users, Collections, members ?? [], groups ?? [], groupMembers ?? []);

    [Fact]
    public void Member_ReadsOpenCollectionsOnly()
    {
        var ana = new OutlineUserDto(Guid.NewGuid(), "ana@example.com", "member", IsSuspended: false);

        var access = Build([ana]);

        Assert.Equal([Handbook], access["ana@example.com"]);
    }

    [Fact]
    public void Viewer_ReadsOpenCollections()
    {
        var vic = new OutlineUserDto(Guid.NewGuid(), "vic@example.com", "viewer", IsSuspended: false);

        Assert.Equal([Handbook], Build([vic])["vic@example.com"]);
    }

    [Fact]
    public void Guest_ReadsOnlyCollectionsTheyWereAddedTo()
    {
        var gus = new OutlineUserDto(Guid.NewGuid(), "gus@example.com", "guest", IsSuspended: false);

        var access = Build([gus], members: new() { [Finance] = [gus.Id] });

        Assert.Equal([Finance], access["gus@example.com"]);
    }

    [Fact]
    public void GroupMember_ReadsPrivateCollectionSharedWithTheGroup()
    {
        var ana = new OutlineUserDto(Guid.NewGuid(), "ana@example.com", "member", IsSuspended: false);
        var bob = new OutlineUserDto(Guid.NewGuid(), "bob@example.com", "member", IsSuspended: false);

        var access = Build(
            [ana, bob],
            groups: new() { [HrPrivate] = [HrGroup] },
            groupMembers: new() { [HrGroup] = [ana.Id] });

        Assert.True(access["ana@example.com"].SetEquals([Handbook, HrPrivate]));
        Assert.Equal([Handbook], access["bob@example.com"]);
    }

    [Fact]
    public void SuspendedUser_IsLeftOut()
    {
        var sam = new OutlineUserDto(Guid.NewGuid(), "sam@example.com", "admin", IsSuspended: true);

        Assert.False(Build([sam]).ContainsKey("sam@example.com"));
    }

    [Fact]
    public void Lookup_IgnoresEmailCase()
    {
        var ana = new OutlineUserDto(Guid.NewGuid(), "Ana@Example.com", "member", IsSuspended: false);

        Assert.True(Build([ana]).ContainsKey("ana@example.COM"));
    }

    [Fact]
    public void UsersWithoutEmail_FailInsteadOfDenyingEveryone()
    {
        // Outline hides emails from non-admin API keys; that misconfiguration must be loud.
        var hidden = new OutlineUserDto(Guid.NewGuid(), null, "member", IsSuspended: false);

        var error = Assert.Throws<InvalidOperationException>(() => Build([hidden]));
        Assert.Contains("admin", error.Message, StringComparison.Ordinal);
    }
}
