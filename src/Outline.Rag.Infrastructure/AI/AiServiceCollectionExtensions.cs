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
                    ChatProvider.Ollama => CreateOllama(chat),
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
                    EmbeddingProvider.Ollama => new OllamaApiClient(
                        CreateOllamaHttpClient(embeddings.Endpoint, embeddings.ApiKey), embeddings.Model),
                    _ => throw new InvalidOperationException($"Unsupported embedding provider '{embeddings.Provider}'."),
                };
            })
            .UseLogging()
            .UseOpenTelemetry();

        return services;
    }

    private static IChatClient CreateOllama(ChatModelOptions chat)
    {
        var httpClient = CreateOllamaHttpClient(
            chat.Endpoint ?? throw new InvalidOperationException("AI:Chat:Endpoint is required for Ollama."),
            chat.ApiKey);
        httpClient.Timeout = chat.Timeout ?? ChatModelOptions.DefaultOllamaTimeout;
        IChatClient client = new OllamaApiClient(httpClient, chat.Model);
        if (chat.Thinking)
        {
            return client;
        }

        // Local thinking models (qwen3.5, deepseek-r1, ...) reason for minutes on CPU/Metal before answering.
        // Answers are grounded in the retrieved excerpts, so turn reasoning off unless AI:Chat:Thinking is set.
        return client.AsBuilder()
            .ConfigureOptions(options => options.Reasoning ??= new ReasoningOptions { Effort = ReasoningEffort.None })
            .Build();
    }

    private static HttpClient CreateOllamaHttpClient(Uri endpoint, string? apiKey)
    {
        var httpClient = new HttpClient { BaseAddress = endpoint };
        if (!string.IsNullOrEmpty(apiKey))
        {
            httpClient.DefaultRequestHeaders.Authorization = new("Bearer", apiKey);
        }

        return httpClient;
    }

    private static IChatClient CreateAnthropic(ChatModelOptions chat)
    {
        // Without an explicit key the SDK resolves ANTHROPIC_API_KEY, ANTHROPIC_BASE_URL, etc. from the environment.
        IAnthropicClient client = string.IsNullOrEmpty(chat.ApiKey)
            ? new AnthropicClient()
            : new AnthropicClient { ApiKey = chat.ApiKey };
        if (chat.Timeout is { } timeout)
        {
            client = client.WithOptions(o => o with { Timeout = timeout });
        }

        return client.AsIChatClient(chat.Model, chat.MaxOutputTokens, AnthropicThinkingMode.Adaptive);
    }
}
