using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Outline.Rag.Application;
using Outline.Rag.Application.Abstractions;
using Outline.Rag.Application.Answering;
using Outline.Rag.Application.Retrieval;
using Outline.Rag.Eval;
using Outline.Rag.Infrastructure;

// Measures retrieval against a dataset of questions with known source documents:
//   dotnet run --project tools/Outline.Rag.Eval -- --email admin@example.com [--dataset path.json] [--top 10] [--rewrite false]
// Needs the RAG database already indexed by the Worker. Answers are not generated; the chat model is only called to
// rewrite follow-up questions (cases with a history), as the chat endpoint does.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});
builder.Configuration.AddCommandLine(args, EvalOptions.SwitchMappings);

// Retrieve without a cut-off, so every threshold can be scored from one pass; the configured one is reported first.
var configuredMinScore = builder.Configuration.GetValue("Rag:MinScore", new RagOptions().MinScore);
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Rag:MinScore"] = "0",
    ["Logging:LogLevel:Default"] = "Warning",
    ["Logging:LogLevel:Microsoft.Extensions.AI"] = "Warning",
});

builder.Services.AddRagInfrastructure(builder.Configuration);
builder.Services.AddOptions<EvalOptions>().BindConfiguration(EvalOptions.SectionName).ValidateDataAnnotations();

using var host = builder.Build();
using var scope = host.Services.CreateScope();
var services = scope.ServiceProvider;
var options = services.GetRequiredService<IOptions<EvalOptions>>().Value;
var top = options.Top ?? services.GetRequiredService<IOptions<RagOptions>>().Value.TopK;

var dataset = await LoadDatasetAsync(options.Dataset).ConfigureAwait(false);
var collections = await services.GetRequiredService<ICollectionAccessResolver>()
    .GetReadableCollectionIdsAsync(options.Email, CancellationToken.None)
    .ConfigureAwait(false);
if (collections.Count == 0)
{
    Console.Error.WriteLine($"{options.Email} can read no Outline collections, so every search would be empty.");
    return 1;
}

var retrieval = services.GetRequiredService<RetrievalService>();
var rewriter = services.GetRequiredService<QuestionRewriter>();
var results = new List<CaseResult>(dataset.Cases.Count);
var elapsed = Stopwatch.StartNew();
foreach (var evalCase in dataset.Cases)
{
    var question = evalCase.Question;
    if (options.Rewrite && evalCase.History is { Count: > 0 } history)
    {
        ChatMessage[] turns = [.. history.Select(t => new ChatMessage(new ChatRole(t.Role), t.Content))];
        question = await rewriter.ToStandaloneAsync(turns, question, CancellationToken.None).ConfigureAwait(false);
    }

    // Same queries as the chat endpoint: the rewrite, plus the follow-up as typed when they differ.
    var retrieved = await retrieval.SearchAsync(AnswerService.Queries(evalCase.Question, question), [.. collections], top, CancellationToken.None)
        .ConfigureAwait(false);
    results.Add(new CaseResult(evalCase, retrieved, question == evalCase.Question ? null : question));
}

elapsed.Stop();

EvalReport.Write(Console.Out, dataset, results, top, configuredMinScore, options.Thresholds, elapsed.Elapsed);
return 0;

static async Task<EvalDataset> LoadDatasetAsync(string path)
{
    var resolved = File.Exists(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
    await using var stream = File.OpenRead(resolved);
    var dataset = await JsonSerializer.DeserializeAsync<EvalDataset>(stream, JsonSerializerOptions.Web).ConfigureAwait(false);
    return dataset is { Cases.Count: > 0 }
        ? dataset
        : throw new InvalidDataException($"Dataset '{resolved}' has no cases.");
}
