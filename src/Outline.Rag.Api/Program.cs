using Outline.Rag.Api.Background;
using Outline.Rag.Api.Endpoints;
using Outline.Rag.Api.Security;
using Outline.Rag.Infrastructure;
using Outline.Rag.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = RequestLimits.MaxRequestBodyBytes);

builder.Services.AddRagInfrastructure(builder.Configuration);
builder.Services.AddHostedService<ReindexBackgroundService>();
builder.Services.AddRagSecurity(builder.Configuration);
builder.Services.AddRagRateLimiting();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.WarnIfNoAuthentication();

await app.Services.GetRequiredService<RagDatabaseInitializer>().InitializeAsync(app.Lifetime.ApplicationStopping);

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapHealthChecks("/health").AllowAnonymous();
app.MapRagEndpoints();
app.MapOpenAiChat();
app.MapOutlineWebhook();

await app.RunAsync();
