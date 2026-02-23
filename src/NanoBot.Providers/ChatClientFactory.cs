using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Configuration;
using OpenAI.Chat;

namespace NanoBot.Providers;

public interface IChatClientFactory
{
    IChatClient CreateChatClient(LlmConfig config);
    IChatClient CreateChatClient(string provider, string model, string? apiKey = null, string? apiBase = null);
}

public class ChatClientFactory : IChatClientFactory
{
    private readonly ILogger<ChatClientFactory> _logger;

    private static readonly Dictionary<string, ProviderSpec> ProviderSpecs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["openai"] = new ProviderSpec(
            EnvKey: "OPENAI_API_KEY",
            DefaultApiBase: "https://api.openai.com/v1",
            DisplayName: "OpenAI"
        ),
        ["openrouter"] = new ProviderSpec(
            EnvKey: "OPENROUTER_API_KEY",
            DefaultApiBase: "https://openrouter.ai/api/v1",
            DisplayName: "OpenRouter",
            LiteLLMPrefix: "openrouter"
        ),
        ["anthropic"] = new ProviderSpec(
            EnvKey: "ANTHROPIC_API_KEY",
            DefaultApiBase: "https://api.anthropic.com/v1",
            DisplayName: "Anthropic"
        ),
        ["deepseek"] = new ProviderSpec(
            EnvKey: "DEEPSEEK_API_KEY",
            DefaultApiBase: "https://api.deepseek.com/v1",
            DisplayName: "DeepSeek",
            LiteLLMPrefix: "deepseek"
        ),
        ["groq"] = new ProviderSpec(
            EnvKey: "GROQ_API_KEY",
            DefaultApiBase: "https://api.groq.com/openai/v1",
            DisplayName: "Groq"
        ),
        ["moonshot"] = new ProviderSpec(
            EnvKey: "MOONSHOT_API_KEY",
            DefaultApiBase: "https://api.moonshot.cn/v1",
            DisplayName: "Moonshot"
        ),
        ["zhipu"] = new ProviderSpec(
            EnvKey: "ZHIPU_API_KEY",
            DefaultApiBase: "https://open.bigmodel.cn/api/paas/v4",
            DisplayName: "Zhipu AI"
        ),
        ["ollama"] = new ProviderSpec(
            EnvKey: "",
            DefaultApiBase: "http://localhost:11434/v1",
            DisplayName: "Ollama",
            IsLocal: true
        ),
        ["volcengine"] = new ProviderSpec(
            EnvKey: "VOLCENGINE_API_KEY",
            DefaultApiBase: "https://ark.cn-beijing.volces.com/api/v3",
            DisplayName: "VolcEngine",
            LiteLLMPrefix: "volcengine"
        ),
        ["siliconflow"] = new ProviderSpec(
            EnvKey: "SILICONFLOW_API_KEY",
            DefaultApiBase: "https://api.siliconflow.cn/v1",
            DisplayName: "SiliconFlow",
            LiteLLMPrefix: "siliconflow"
        )
    };

    public ChatClientFactory(ILogger<ChatClientFactory> logger)
    {
        _logger = logger;
    }

    public IChatClient CreateChatClient(LlmConfig config)
    {
        var provider = config.Provider ?? "openai";
        return CreateChatClient(provider, config.Model, config.ApiKey, config.ApiBase);
    }

    public IChatClient CreateChatClient(string provider, string model, string? apiKey = null, string? apiBase = null)
    {
        if (!ProviderSpecs.TryGetValue(provider, out var spec))
        {
            throw new NotSupportedException($"Provider '{provider}' is not supported. Supported providers: {string.Join(", ", ProviderSpecs.Keys)}");
        }

        var resolvedApiKey = apiKey ?? Environment.GetEnvironmentVariable(spec.EnvKey) ?? "";
        var resolvedApiBase = apiBase ?? spec.DefaultApiBase;

        _logger.LogInformation("Creating ChatClient for provider {Provider} with model {Model}", provider, model);

        var resolvedModel = ResolveModel(model, spec.LiteLLMPrefix);

        if (spec.IsLocal && string.IsNullOrEmpty(resolvedApiKey))
        {
            resolvedApiKey = "local-no-key";
        }

        var clientOptions = new OpenAI.OpenAIClientOptions 
        { 
            Endpoint = new Uri(resolvedApiBase),
            NetworkTimeout = TimeSpan.FromSeconds(60)
        };

        var client = new OpenAI.OpenAIClient(
            new System.ClientModel.ApiKeyCredential(resolvedApiKey),
            clientOptions
        );

        var baseClient = client.GetChatClient(resolvedModel).AsIChatClient();

        _logger.LogInformation("[TIMING] Created base ChatClient for model {Model}", resolvedModel);

        IChatClient chatClient = new ChatClientBuilder(baseClient)
            .UseFunctionInvocation(loggerFactory: null)
            .Build();

        _logger.LogInformation("[TIMING] Added FunctionInvocation middleware");

        var sanitizingClient = new SanitizingChatClient(chatClient, _logger);
        _logger.LogInformation("[TIMING] Created sanitizing client");

        return new InterimTextRetryChatClient(sanitizingClient, _logger);
    }

    private static string ResolveModel(string model, string? liteLLMPrefix)
    {
        if (string.IsNullOrEmpty(liteLLMPrefix))
            return model;

        if (model.Contains('/'))
            return model;

        return $"{liteLLMPrefix}/{model}";
    }

    private record ProviderSpec(
        string EnvKey,
        string DefaultApiBase,
        string DisplayName,
        string? LiteLLMPrefix = null,
        bool IsLocal = false
    );
}
