using Outline.Rag.Infrastructure.Outline;

namespace Outline.Rag.UnitTests.Outline;

public sealed class OutlineWebhookSignatureTests
{
    private const string Secret = "ol_whs_test";
    private const string Body = """{"event":"documents.update","payload":{"id":"11111111-1111-1111-1111-111111111111"}}""";

    [Fact]
    public void IsValid_AcceptsMatchingSignature()
    {
        var header = $"t=1758800000000,s={OutlineWebhookSignature.Compute("1758800000000", Body, Secret)}";

        Assert.True(OutlineWebhookSignature.IsValid(header, Body, Secret));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("t=1758800000000")]
    [InlineData("t=1758800000000,s=deadbeef")]
    public void IsValid_RejectsMissingOrWrongSignature(string? header)
    {
        Assert.False(OutlineWebhookSignature.IsValid(header, Body, Secret));
    }

    [Fact]
    public void IsValid_RejectsTamperedBody()
    {
        var header = $"t=1,s={OutlineWebhookSignature.Compute("1", Body, Secret)}";

        Assert.False(OutlineWebhookSignature.IsValid(header, Body.Replace("update", "delete", StringComparison.Ordinal), Secret));
    }
}
