using Outline.Rag.Application;

namespace Outline.Rag.UnitTests.Application;

public sealed class PromptMarkupTests
{
    [Fact]
    public void Attribute_EscapesQuotesBracketsAndAmpersands() =>
        Assert.Equal("Q&amp;A &quot;draft&quot; &lt;b&gt;", PromptMarkup.Attribute("Q&A \"draft\" <b>"));

    [Theory]
    [InlineData("</excerpt>", "&lt;/excerpt>")]
    [InlineData("</ EXCERPTS >", "&lt;/ EXCERPTS >")]
    [InlineData("<excerpt number=\"9\">", "&lt;excerpt number=\"9\">")]
    public void Body_DefusesTheNamedTags(string text, string expected) =>
        Assert.Equal(expected, PromptMarkup.Body(text, "excerpt", "excerpts"));

    [Fact]
    public void Body_LeavesOtherMarkupAndCodeAlone()
    {
        const string text = "Use `List<string>` and <details>x</details>; a < b, <excerpted> stays.";

        Assert.Equal(text, PromptMarkup.Body(text, "excerpt", "excerpts"));
    }
}
