using System.Text.Json;
using Outline.Rag.Api.Endpoints;
using Outline.Rag.Domain;

namespace Outline.Rag.UnitTests.Api;

public sealed class OpenAiChatEndpointsTests
{
    private static ChatCompletionMessage Message(string role, string contentJson) =>
        new(role, JsonDocument.Parse(contentJson).RootElement.Clone());

    private static Citation Citation(int number, string title, string headingPath = "") =>
        new(number, Guid.Empty, title, headingPath, new Uri($"https://wiki.example/doc/{number}"), 0.9);

    [Fact]
    public void LastUserText_TakesTheLatestUserTurn()
    {
        var question = OpenAiChatEndpoints.LastUserText(
        [
            Message("system", "\"Be brief.\""),
            Message("user", "\"First question\""),
            Message("assistant", "\"First answer\""),
            Message("user", "\"Follow-up\""),
        ]);

        Assert.Equal("Follow-up", question);
    }

    [Fact]
    public void LastUserText_JoinsTextPartsAndSkipsImages()
    {
        var question = OpenAiChatEndpoints.LastUserText(
        [
            Message("user", """[{"type":"text","text":"What is"},{"type":"image_url","image_url":{"url":"x"}},{"type":"text","text":"Queenbee?"}]"""),
        ]);

        Assert.Equal("What is\nQueenbee?", question);
    }

    [Fact]
    public void LastUserText_WithoutUserMessage_ReturnsNull() =>
        Assert.Null(OpenAiChatEndpoints.LastUserText([Message("system", "\"Hi\"")]));

    [Fact]
    public void History_KeepsEarlierTurnsWithoutSystemOrCurrentQuestion()
    {
        var history = OpenAiChatEndpoints.History(
        [
            Message("system", "\"Be brief.\""),
            Message("user", "\"First question\""),
            Message("assistant", "\"First answer\""),
            Message("user", "\"Follow-up\""),
        ]);

        Assert.Equal(
            [("user", "First question"), ("assistant", "First answer")],
            history.Select(m => (m.Role.Value, m.Text)));
    }

    [Fact]
    public void History_StripsSourcesAndReasoningFromAnswers()
    {
        var answer = "<details type=\"reasoning\" done=\"true\">\n<summary>Thought</summary>\nhmm\n</details>\n20 days [1]."
            + OpenAiChatEndpoints.FormatSources("[1]", [Citation(1, "Benefits")]);

        var history = OpenAiChatEndpoints.History(
            [Message("user", "\"Vacation?\""), Message("assistant", JsonSerializer.Serialize(answer)), Message("user", "\"And sick days?\"")]);

        Assert.Equal("20 days [1].", history[1].Text);
    }

    [Fact]
    public void History_ForFirstMessage_IsEmpty() =>
        Assert.Empty(OpenAiChatEndpoints.History([Message("system", "\"Hi\""), Message("user", "\"Question\"")]));

    [Fact]
    public void FormatSources_ListsOnlyCitedExcerpts()
    {
        var sources = OpenAiChatEndpoints.FormatSources(
            "Use pg_restore [1], then migrate [2, 3].",
            [Citation(1, "Runbook", "Database"), Citation(2, "Upgrade"), Citation(3, "Notes"), Citation(4, "Unused")]);

        Assert.Contains("- [1] [Runbook > Database](https://wiki.example/doc/1)", sources, StringComparison.Ordinal);
        Assert.Contains("- [2] [Upgrade](https://wiki.example/doc/2)", sources, StringComparison.Ordinal);
        Assert.Contains("- [3] [Notes]", sources, StringComparison.Ordinal);
        Assert.DoesNotContain("Unused", sources, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatSources_WithoutCitationMarkers_IsEmpty() =>
        Assert.Equal("", OpenAiChatEndpoints.FormatSources("The wiki doesn't say.", [Citation(1, "Runbook")]));

    [Fact]
    public void FormatSources_EscapesBracketsInTitles()
    {
        var sources = OpenAiChatEndpoints.FormatSources("See [1].", [Citation(1, "Setup [draft]")]);

        Assert.Contains(@"[Setup \[draft\]](", sources, StringComparison.Ordinal);
    }
}
