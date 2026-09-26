using Outline.Rag.Api.Background;
using Outline.Rag.Api.Endpoints;
using Outline.Rag.Infrastructure;
using Outline.Rag.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRagInfrastructure(builder.Configuration);
builder.Services.AddHostedService<ReindexBackgroundService>();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

await app.Services.GetRequiredService<RagDatabaseInitializer>().InitializeAsync(app.Lifetime.ApplicationStopping);

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health");
app.MapRagEndpoints();
app.MapOutlineWebhook();

await app.RunAsync();
