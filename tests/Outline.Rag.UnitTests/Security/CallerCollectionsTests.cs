using System.Security.Claims;
using NSubstitute;
using Outline.Rag.Api.Security;
using Outline.Rag.Application.Abstractions;

namespace Outline.Rag.UnitTests.Security;

public sealed class CallerCollectionsTests
{
    private static readonly Guid Handbook = Guid.NewGuid();
    private static readonly Guid HrPrivate = Guid.NewGuid();

    private readonly ICollectionAccessResolver _resolver = Substitute.For<ICollectionAccessResolver>();

    public CallerCollectionsTests()
    {
        _resolver.GetReadableCollectionIdsAsync("ana@example.com", Arg.Any<CancellationToken>())
            .Returns(new HashSet<Guid> { Handbook });
    }

    private static ClaimsPrincipal Caller(string? email) =>
        new(new ClaimsIdentity(email is null ? [] : [new Claim(RagClaims.Email, email)], "test"));

    [Fact]
    public async Task WithoutRequestedCollections_ReturnsEverythingReadable()
    {
        var collections = await new CallerCollections(_resolver)
            .ResolveAsync(Caller("ana@example.com"), requested: null, TestContext.Current.CancellationToken);

        Assert.Equal([Handbook], collections);
    }

    [Fact]
    public async Task RequestedCollections_AreNarrowedToReadableOnes()
    {
        var collections = await new CallerCollections(_resolver)
            .ResolveAsync(Caller("ana@example.com"), [HrPrivate, Handbook], TestContext.Current.CancellationToken);

        Assert.Equal([Handbook], collections);
    }

    [Fact]
    public async Task RequestingOnlyUnreadableCollections_ReturnsNothing()
    {
        // Empty must stay empty: it is not "no filter".
        var collections = await new CallerCollections(_resolver)
            .ResolveAsync(Caller("ana@example.com"), [HrPrivate], TestContext.Current.CancellationToken);

        Assert.Empty(collections);
    }

    [Fact]
    public async Task CallerWithoutEmail_ReadsNothing()
    {
        var collections = await new CallerCollections(_resolver)
            .ResolveAsync(Caller(null), requested: null, TestContext.Current.CancellationToken);

        Assert.Empty(collections);
        await _resolver.DidNotReceive().GetReadableCollectionIdsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("s3cret-gateway-key", true)]
    [InlineData("s3cret-gateway-ke", false)]
    [InlineData("", false)]
    public void GatewayKey_MustMatchExactly(string presented, bool expected) =>
        Assert.Equal(expected, GatewayAuthenticationHandler.KeyMatches(presented, "s3cret-gateway-key"));
}
