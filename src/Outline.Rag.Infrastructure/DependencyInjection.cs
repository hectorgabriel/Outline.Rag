using CommunityToolkit.VectorData.PgVector;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.ML.Tokenizers;
using Npgsql;
using Outline.Rag.Application;
using Outline.Rag.Application.Abstractions;
using Outline.Rag.Infrastructure.AI;
using Outline.Rag.Infrastructure.Chunking;
using Outline.Rag.Infrastructure.Outline;
using Outline.Rag.Infrastructure.Persistence;
using Outline.Rag.Infrastructure.VectorStore;

namespace Outline.Rag.Infrastructure;

public static class DependencyInjection
{
    public const string RagDatabaseConnectionName = "RagDatabase";

    /// <summary>
    /// Registers the application layer plus every adapter: Outline API, chunker, pgvector index and AI providers.
    /// </summary>
    public static IServiceCollection AddRagInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddApplication();

        services.AddOptions<OutlineOptions>().BindConfiguration(OutlineOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<VectorStoreOptions>().BindConfiguration(VectorStoreOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<AiOptions>().BindConfiguration(AiOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();

        services.AddHttpClient<IOutlineDocumentSource, OutlineApiClient>((sp, http) =>
            {
                var outline = sp.GetRequiredService<IOptions<OutlineOptions>>().Value;
                http.BaseAddress = outline.BaseUrl;
                http.DefaultRequestHeaders.Authorization = new("Bearer", outline.ApiToken);
            })
            .AddStandardResilienceHandler();

        services.AddSingleton<Tokenizer>(_ => TiktokenTokenizer.CreateForModel("gpt-4"));
        services.AddSingleton<IDocumentChunker, MarkdownHeadingChunker>();

        var connectionString = configuration.GetConnectionString(RagDatabaseConnectionName)
            ?? throw new InvalidOperationException($"Connection string '{RagDatabaseConnectionName}' is not configured.");
        services.AddSingleton(_ =>
        {
            var builder = new NpgsqlDataSourceBuilder(connectionString);
            builder.UseVector();
            return builder.Build();
        });

        services.AddSingleton(sp =>
        {
            var dimensions = sp.GetRequiredService<IOptions<AiOptions>>().Value.Embeddings.Dimensions;
            var name = sp.GetRequiredService<IOptions<VectorStoreOptions>>().Value.CollectionName;
            return new PostgresCollection<Guid, ChunkRecord>(
                sp.GetRequiredService<NpgsqlDataSource>(),
                name,
                ownsDataSource: false,
                new PostgresCollectionOptions { Definition = ChunkRecordDefinition.Create(dimensions) });
        });
        services.AddSingleton<IChunkIndex, PgVectorChunkIndex>();
        services.AddSingleton<NpgsqlSyncCheckpointStore>();
        services.AddSingleton<ISyncCheckpointStore>(sp => sp.GetRequiredService<NpgsqlSyncCheckpointStore>());
        services.AddSingleton<NpgsqlIndexedDocumentStore>();
        services.AddSingleton<IIndexedDocumentStore>(sp => sp.GetRequiredService<NpgsqlIndexedDocumentStore>());
        services.AddSingleton(sp => new RagDatabaseInitializer(
            sp.GetRequiredService<IChunkIndex>(),
            sp.GetRequiredService<NpgsqlSyncCheckpointStore>(),
            sp.GetRequiredService<NpgsqlIndexedDocumentStore>()));

        services.AddAiProviders();
        return services;
    }
}
