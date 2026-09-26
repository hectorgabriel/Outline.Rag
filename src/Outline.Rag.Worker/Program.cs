using Outline.Rag.Infrastructure;
using Outline.Rag.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddRagInfrastructure(builder.Configuration);
builder.Services.AddOptions<SyncOptions>().BindConfiguration(SyncOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddHostedService<OutlineSyncWorker>();

await builder.Build().RunAsync();
