using Microsoft.Extensions.DependencyInjection;
using Outline.Rag.Application.Answering;
using Outline.Rag.Application.Ingestion;
using Outline.Rag.Application.Retrieval;

namespace Outline.Rag.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddOptions<RagOptions>()
            .BindConfiguration(RagOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<ReindexChannel>();
        services.AddScoped<DocumentIngestionService>();
        services.AddScoped<OutlineSyncService>();
        services.AddScoped<RetrievalService>();
        services.AddScoped<AnswerService>();
        return services;
    }
}
