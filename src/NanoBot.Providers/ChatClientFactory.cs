using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Configuration;
using NanoBot.Providers.Decorators;
using OpenAI.Chat;
using System.Net;

namespace NanoBot.Providers;

public interface IChatClientFactory
{
    IChatClient CreateChatClient(LlmConfig config);
    IChatClient CreateChatClient(string provider, string model, string? apiKey = null, string? apiBase = null, int? maxTokens = null);
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
        ),
        ["stepfun"] = new ProviderSpec(
            EnvKey: "STEPFUN_API_KEY",
            DefaultApiBase: "https://api.stepfun.com/v1",
            DisplayName: "Step Fun"
        )
    };

    public ChatClientFactory(ILogger<ChatClientFactory> logger)
    {
        _logger = logger;
    }

    public IChatClient CreateChatClient(LlmConfig config)
    {
        var profileName = string.IsNullOrEmpty(config.DefaultProfile) ? "default" : config.DefaultProfile;
        if (!config.Profiles.TryGetValue(profileName, out var profile))
        {
            throw new InvalidOperationException($"LLM profile '{profileName}' not found in configuration");
        }
        
        var provider = profile.Provider ?? "openai";
        return CreateChatClient(provider, profile.Model, profile.ApiKey, profile.ApiBase, profile.MaxTokens);
    }

    public IChatClient CreateChatClient(string provider, string model, string? apiKey = null, string? apiBase = null, int? maxTokens = null)
    {
        if (!ProviderSpecs.TryGetValue(provider, out var spec))
        {
            throw new NotSupportedException($"Provider '{provider}' is not supported. Supported providers: {string.Join(", ", ProviderSpecs.Keys)}");
        }

        var resolvedApiKey = string.IsNullOrWhiteSpace(apiKey)
            ? (Environment.GetEnvironmentVariable(spec.EnvKey) ?? "")
            : apiKey;
        var resolvedApiBase = apiBase ?? spec.DefaultApiBase;
        var normalizedApiBase = NormalizeApiBaseForConnectivity(resolvedApiBase);

        _logger.LogInformation("Creating ChatClient for provider {Provider} with model {Model} apiBase={ApiBase} normalizedApiBase={NormalizedApiBase}", provider, model, resolvedApiBase, normalizedApiBase);

        var resolvedModel = ResolveModel(model, spec.LiteLLMPrefix);

        if (spec.IsLocal && string.IsNullOrEmpty(resolvedApiKey))
        {
            resolvedApiKey = "local-no-key";
        }

        if (!spec.IsLocal && string.IsNullOrWhiteSpace(resolvedApiKey))
        {
            throw new InvalidOperationException(
                $"Missing API key for provider '{provider}'. Set it in config (llm.profiles.default.api_key/apiKey/ApiKey) or via environment variable '{spec.EnvKey}'.");
        }

        // Use longer timeout for local models (Ollama) as they may need more time for large images/vision tasks
        var networkTimeout = spec.IsLocal ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(60);

        // Log info about o1 models needing max_completion_tokens
        if (maxTokens.HasValue && (
            resolvedModel.StartsWith("o1", StringComparison.OrdinalIgnoreCase) ||
            resolvedModel.StartsWith("o3", StringComparison.OrdinalIgnoreCase) ||
            resolvedModel.StartsWith("o4", StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogInformation("Model {Model} is an o-series model. maxTokens={MaxTokens} will be passed via ChatOptions.MaxOutputTokens", 
                resolvedModel, maxTokens.Value);
        }

        var clientOptions = new OpenAI.OpenAIClientOptions 
        { 
            Endpoint = new Uri(normalizedApiBase),
            NetworkTimeout = networkTimeout
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

        var interimClient = new InterimTextRetryChatClient(sanitizingClient, _logger);
        return new EmptyChoicesProtectionChatClient(interimClient);
    }

    private string NormalizeApiBaseForConnectivity(string apiBase)
    {
        if (!Uri.TryCreate(apiBase, UriKind.Absolute, out var uri))
        {
            return apiBase;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return apiBase;
        }

        if (IPAddress.TryParse(uri.Host, out _))
        {
            return apiBase;
        }

        try
        {
            var addresses = Dns.GetHostAddresses(uri.Host);
            var ipv4 = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            if (ipv4 == null)
            {
                return apiBase;
            }

            var builder = new UriBuilder(uri)
            {
                Host = ipv4.ToString()
            };

            var normalized = builder.Uri.ToString();
            _logger.LogInformation("Resolved apiBase host {Host} to IPv4 {IPv4} for connectivity; normalizedApiBase={NormalizedApiBase}", uri.Host, ipv4, normalized);
            return normalized;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to normalize apiBase host {Host}; using original apiBase {ApiBase}", uri.Host, apiBase);
            return apiBase;
        }
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
