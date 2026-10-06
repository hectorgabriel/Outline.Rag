using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Outline.Rag.Api.Security;
using Outline.Rag.Application.Answering;
using Outline.Rag.Application.Retrieval;
using Outline.Rag.Domain;

namespace Outline.Rag.Api.Endpoints;

/// <param name="CollectionIds">Narrows the search to these collections; ones the caller can't read are ignored.</param>
public sealed record SearchRequest(
    [property: Required, StringLength(RequestLimits.MaxQuestionLength, MinimumLength = 1)] string Query,
    [property: Range(1, RequestLimits.MaxTop)] int? Top,
    [property: MaxLength(RequestLimits.MaxCollectionIds)] Guid[]? CollectionIds);

/// <param name="CollectionIds">Narrows the search to these collections; ones the caller can't read are ignored.</param>
public sealed record AskRequest(
    [property: Required, StringLength(RequestLimits.MaxQuestionLength, MinimumLength = 1)] string Question,
    [property: MaxLength(RequestLimits.MaxCollectionIds)] Guid[]? CollectionIds);

internal static class RagEndpoints
{
    public static IEndpointRouteBuilder MapRagEndpoints(this IEndpointRouteBuilder app)
    {
        // Callers only ever search the collections they can read in Outline (see CallerCollections).
        var group = app.MapGroup("/api").WithTags("RAG");

        group.MapPost("/search", async (
                SearchRequest request, ClaimsPrincipal caller, CallerCollections callerCollections,
                RetrievalService retrieval, CancellationToken ct) =>
            {
                var collections = await callerCollections.ResolveAsync(caller, request.CollectionIds, ct);
                var results = await retrieval.SearchAsync(request.Query, collections, request.Top, ct);
                return TypedResults.Ok(results);
            })
            .AddEndpointFilter<ValidateRequest<SearchRequest>>()
            .RequireRateLimiting(RagRateLimiting.SearchPolicy)
            .WithName("Search")
            .WithSummary("Semantic search over the indexed Outline chunks the caller can read.")
            .Produces<IReadOnlyList<RetrievedChunk>>();

        group.MapPost("/ask", async (
                AskRequest request, ClaimsPrincipal caller, CallerCollections callerCollections,
                AnswerService answers, CancellationToken ct) =>
            {
                var collections = await callerCollections.ResolveAsync(caller, request.CollectionIds, ct);
                var answer = await answers.AskAsync(request.Question, collections, ct);
                return TypedResults.Ok(answer);
            })
            .AddEndpointFilter<ValidateRequest<AskRequest>>()
            .RequireRateLimiting(RagRateLimiting.AnswerPolicy)
            .WithName("Ask")
            .WithSummary("Answer a question from the parts of the wiki the caller can read, with citations.")
            .Produces<RagAnswer>();

        return app;
    }
}
