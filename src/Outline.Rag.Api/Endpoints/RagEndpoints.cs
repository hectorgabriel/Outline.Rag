using System.ComponentModel.DataAnnotations;
using Outline.Rag.Application.Answering;
using Outline.Rag.Application.Retrieval;
using Outline.Rag.Domain;

namespace Outline.Rag.Api.Endpoints;

public sealed record SearchRequest([property: Required, MinLength(1)] string Query, int? Top, Guid[]? CollectionIds);

public sealed record AskRequest([property: Required, MinLength(1)] string Question, Guid[]? CollectionIds);

internal static class RagEndpoints
{
    public static IEndpointRouteBuilder MapRagEndpoints(this IEndpointRouteBuilder app)
    {
        // TODO: authenticate callers (same OIDC provider as Outline) and derive CollectionIds from the caller's
        // Outline permissions instead of trusting the request; today every caller sees every indexed collection.
        var group = app.MapGroup("/api").WithTags("RAG");

        group.MapPost("/search", async (SearchRequest request, RetrievalService retrieval, CancellationToken ct) =>
            {
                var results = await retrieval.SearchAsync(request.Query, request.CollectionIds ?? [], request.Top, ct);
                return TypedResults.Ok(results);
            })
            .WithName("Search")
            .WithSummary("Semantic search over indexed Outline chunks.")
            .Produces<IReadOnlyList<RetrievedChunk>>();

        group.MapPost("/ask", async (AskRequest request, AnswerService answers, CancellationToken ct) =>
            {
                var answer = await answers.AskAsync(request.Question, request.CollectionIds ?? [], ct);
                return TypedResults.Ok(answer);
            })
            .WithName("Ask")
            .WithSummary("Answer a question from the wiki, with citations.")
            .Produces<RagAnswer>();

        return app;
    }
}
