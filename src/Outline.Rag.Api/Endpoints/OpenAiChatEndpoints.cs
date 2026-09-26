using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Outline.Rag.Application.Answering;
using Outline.Rag.Domain;

namespace Outline.Rag.Api.Endpoints;

public sealed record ChatCompletionRequest(string? Model, IReadOnlyList<ChatCompletionMessage>? Messages, bool Stream = false);

/// <param name="Content">A string, or an array of parts such as <c>{ "type": "text", "text": "..." }</c>.</param>
public sealed record ChatCompletionMessage(string Role, JsonElement Content);

/// <summary>
/// The subset of the OpenAI Chat Completions API that chat front ends such as Open WebUI need, so they can offer
/// the wiki as a "model". Each request answers the latest user message with <see cref="AnswerService"/>; earlier
/// turns are not used for retrieval, so follow-ups must be self-contained.
/// </summary>
internal static partial class OpenAiChatEndpoints
{
    public const string ModelId = "outline-wiki";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static IEndpointRouteBuilder MapOpenAiChat(this IEndpointRouteBuilder app)
    {
        // Same access model as /api/ask: no caller authentication yet (see the TODO in RagEndpoints).
        var group = app.MapGroup("/v1").WithTags("OpenAI-compatible");

        group.MapGet("/models", () => TypedResults.Json(
                new ModelList("list", [new ModelInfo(ModelId, "model", 0, "outline-rag")]), Json))
            .WithSummary("Lists the single RAG model, for OpenAI-compatible chat UIs.");

        group.MapPost("/chat/completions", async (ChatCompletionRequest request, AnswerService answers, HttpContext http) =>
            {
                var question = LastUserText(request.Messages ?? []);
                if (string.IsNullOrWhiteSpace(question))
                {
                    return Results.Problem("The request needs a user message with text.", statusCode: StatusCodes.Status400BadRequest);
                }

                var ct = http.RequestAborted;
                var id = $"chatcmpl-{Guid.NewGuid():N}";
                var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                if (!request.Stream)
                {
                    var answer = await answers.AskAsync(question, [], ct);
                    var content = answer.Answer + FormatSources(answer.Answer, answer.Citations);
                    return TypedResults.Json(
                        new ChatCompletion(id, "chat.completion", created, ModelId,
                            [new Choice(0, Message: new ChoiceMessage("assistant", content), FinishReason: "stop")]),
                        Json);
                }

                var stream = await answers.AskStreamingAsync(question, [], ct);
                await WriteEventStreamAsync(http.Response, stream, id, created, ct);
                return Results.Empty;
            })
            .WithSummary("Answers the latest user message from the wiki, OpenAI Chat Completions style (JSON or SSE).");

        return app;
    }

    private static async Task WriteEventStreamAsync(
        HttpResponse response, StreamingRagAnswer answer, string id, long created, CancellationToken ct)
    {
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";

        Task Send(ChoiceMessage delta, string? finishReason = null) => WriteEventAsync(
            response,
            JsonSerializer.Serialize(
                new ChatCompletion(id, "chat.completion.chunk", created, ModelId,
                    [new Choice(0, Delta: delta, FinishReason: finishReason)]),
                Json),
            ct);

        await Send(new ChoiceMessage("assistant", ""));

        var text = new StringBuilder();
        await foreach (var piece in answer.Text.WithCancellation(ct))
        {
            text.Append(piece);
            await Send(new ChoiceMessage(null, piece));
        }

        var sources = FormatSources(text.ToString(), answer.Citations);
        if (sources.Length > 0)
        {
            await Send(new ChoiceMessage(null, sources));
        }

        await Send(new ChoiceMessage(null, null), "stop");
        await WriteEventAsync(response, "[DONE]", ct);
    }

    private static async Task WriteEventAsync(HttpResponse response, string data, CancellationToken ct)
    {
        await response.WriteAsync($"data: {data}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    internal static string? LastUserText(IReadOnlyList<ChatCompletionMessage> messages)
    {
        var content = messages.LastOrDefault(m => m.Role == "user")?.Content;
        return content?.ValueKind switch
        {
            JsonValueKind.String => content.Value.GetString(),
            JsonValueKind.Array => string.Join('\n', content.Value.EnumerateArray()
                .Where(part => part.TryGetProperty("type", out var type) && type.ValueEquals("text")
                    && part.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                .Select(part => part.GetProperty("text").GetString())),
            _ => null,
        };
    }

    /// <summary>
    /// Markdown list of the citations the answer actually uses ([2], [1, 3], ...), with links to Outline, or ""
    /// when it cites none (e.g. "the wiki doesn't say").
    /// </summary>
    internal static string FormatSources(string answer, IReadOnlyList<Citation> citations)
    {
        var cited = CitationMarker().Matches(answer)
            .SelectMany(m => m.Groups[1].Value.Split(','))
            .Select(n => int.Parse(n, NumberStyles.Integer, CultureInfo.InvariantCulture))
            .ToHashSet();
        var used = citations.Where(c => cited.Contains(c.Number)).ToList();
        if (used.Count == 0)
        {
            return "";
        }

        var sb = new StringBuilder("\n\n---\n**Sources**\n");
        foreach (var c in used)
        {
            var label = string.IsNullOrEmpty(c.HeadingPath) ? c.Title : $"{c.Title} > {c.HeadingPath}";
            label = label.Replace("[", "\\[", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal);
            sb.Append(CultureInfo.InvariantCulture, $"\n- [{c.Number}] [{label}]({c.Url})");
        }

        return sb.ToString();
    }

    [GeneratedRegex(@"\[(\d+(?:\s*,\s*\d+)*)\]")]
    private static partial Regex CitationMarker();

    private sealed record ModelList(string Object, IReadOnlyList<ModelInfo> Data);

    private sealed record ModelInfo(
        string Id, string Object, long Created, [property: JsonPropertyName("owned_by")] string OwnedBy);

    private sealed record ChatCompletion(string Id, string Object, long Created, string Model, IReadOnlyList<Choice> Choices);

    private sealed record Choice(
        int Index,
        ChoiceMessage? Message = null,
        ChoiceMessage? Delta = null,
        [property: JsonPropertyName("finish_reason")] string? FinishReason = null);

    private sealed record ChoiceMessage(string? Role, string? Content);
}
