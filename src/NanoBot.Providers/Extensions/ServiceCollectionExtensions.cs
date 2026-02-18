using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NanoBot.Core.Configuration;

namespace NanoBot.Providers.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddChatClientFactory(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IChatClientFactory, ChatClientFactory>();

        var llmConfig = configuration.GetSection("LLM").Get<LlmConfig>();
        if (llmConfig is not null)
        {
            services.AddSingleton(llmConfig);

            services.AddSingleton<IChatClient>(sp =>
            {
                var factory = sp.GetRequiredService<IChatClientFactory>();
                return factory.CreateChatClient(llmConfig);
            });
        }

        return services;
    }

    public static IServiceCollection AddChatClient(
        this IServiceCollection services,
        string provider,
        string model,
        string? apiKey = null,
        string? apiBase = null)
    {
        services.AddSingleton<IChatClient>(sp =>
        {
            var factory = sp.GetRequiredService<IChatClientFactory>();
            return factory.CreateChatClient(provider, model, apiKey, apiBase);
        });

        return services;
    }
}
