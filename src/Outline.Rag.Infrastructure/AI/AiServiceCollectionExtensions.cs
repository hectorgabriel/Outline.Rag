using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OllamaSharp;

namespace Outline.Rag.Infrastructure.AI;

/// <summary>
/// Registers <see cref="IChatClient"/> and <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> for the configured
/// providers. Everything above Infrastructure depends only on the Microsoft.Extensions.AI abstractions.
/// </summary>
internal static class AiServiceCollectionExtensions
{
    public static IServiceCollection AddAiProviders(this IServiceCollection services)
    {
        services.AddChatClient(sp =>
            {
                var chat = sp.GetRequiredService<IOptions<AiOptions>>().Value.Chat;
                return chat.Provider switch
                {
                    ChatProvider.Anthropic => CreateAnthropic(chat),
                    ChatProvider.Ollama => new OllamaApiClient(
                        chat.Endpoint ?? throw new InvalidOperationException("AI:Chat:Endpoint is required for Ollama."),
                        chat.Model),
                    _ => throw new InvalidOperationException($"Unsupported chat provider '{chat.Provider}'."),
                };
            })
            .UseLogging()
            .UseOpenTelemetry();

        services.AddEmbeddingGenerator(sp =>
            {
                var embeddings = sp.GetRequiredService<IOptions<AiOptions>>().Value.Embeddings;
                return embeddings.Provider switch
                {
                    EmbeddingProvider.Ollama => new OllamaApiClient(embeddings.Endpoint, embeddings.Model),
                    _ => throw new InvalidOperationException($"Unsupported embedding provider '{embeddings.Provider}'."),
                };
            })
            .UseLogging()
            .UseOpenTelemetry();

        return services;
    }

    private static IChatClient CreateAnthropic(ChatModelOptions chat)
    {
        // Without an explicit key the SDK resolves ANTHROPIC_API_KEY, ANTHROPIC_BASE_URL, etc. from the environment.
        var client = string.IsNullOrEmpty(chat.ApiKey)
            ? new AnthropicClient()
            : new AnthropicClient { ApiKey = chat.ApiKey };

        return client.AsIChatClient(chat.Model, chat.MaxOutputTokens, AnthropicThinkingMode.Adaptive);
    }
}
