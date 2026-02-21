namespace NanoBot.Core.Configuration;

public class ConfigurationCheckResult
{
    public bool ConfigExists { get; init; }
    public bool HasValidLlm { get; init; }
    public bool HasApiKey { get; init; }
    public string? ConfigPath { get; init; }
    public IReadOnlyList<string> MissingFields { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public bool IsReady => ConfigExists && HasValidLlm && HasApiKey;

    public string GetGuidanceMessage()
    {
        if (!ConfigExists)
        {
            return "Configuration file not found. Run 'nbot configure' to set up your configuration.";
        }

        if (!HasValidLlm)
        {
            return "LLM model is not configured. Run 'nbot configure' to set up your LLM provider.";
        }

        if (!HasApiKey)
        {
            return "API key is not configured. Run 'nbot configure' to set up your API key.";
        }

        return string.Empty;
    }
}

public static class ConfigurationChecker
{
    public static readonly IReadOnlyList<string> SupportedProviders = new[]
    {
        "openai",
        "anthropic",
        "openrouter",
        "deepseek",
        "moonshot",
        "zhipu",
        "ollama"
    };

    public static readonly IReadOnlyDictionary<string, string> ProviderEnvKeys = new Dictionary<string, string>
    {
        ["openai"] = "OPENAI_API_KEY",
        ["anthropic"] = "ANTHROPIC_API_KEY",
        ["openrouter"] = "OPENROUTER_API_KEY",
        ["deepseek"] = "DEEPSEEK_API_KEY",
        ["moonshot"] = "MOONSHOT_API_KEY",
        ["zhipu"] = "ZHIPU_API_KEY"
    };

    public static readonly IReadOnlyDictionary<string, string> ProviderDefaultModels = new Dictionary<string, string>
    {
        ["openai"] = "gpt-4o-mini",
        ["anthropic"] = "claude-3-5-sonnet-20241022",
        ["openrouter"] = "anthropic/claude-3.5-sonnet",
        ["deepseek"] = "deepseek-chat",
        ["moonshot"] = "moonshot-v1-8k",
        ["zhipu"] = "glm-4-flash",
        ["ollama"] = "llama3.2"
    };

    public static readonly IReadOnlyDictionary<string, string> ProviderApiBases = new Dictionary<string, string>
    {
        ["openai"] = "https://api.openai.com/v1",
        ["anthropic"] = "https://api.anthropic.com/v1",
        ["openrouter"] = "https://openrouter.ai/api/v1",
        ["deepseek"] = "https://api.deepseek.com/v1",
        ["moonshot"] = "https://api.moonshot.cn/v1",
        ["zhipu"] = "https://open.bigmodel.cn/api/paas/v4",
        ["ollama"] = "http://localhost:11434/v1"
    };

    public static readonly IReadOnlyDictionary<string, string> ProviderKeyUrls = new Dictionary<string, string>
    {
        ["openai"] = "https://platform.openai.com/api-keys",
        ["anthropic"] = "https://console.anthropic.com/settings/keys",
        ["openrouter"] = "https://openrouter.ai/keys",
        ["deepseek"] = "https://platform.deepseek.com/api_keys",
        ["moonshot"] = "https://platform.moonshot.cn/api-keys",
        ["zhipu"] = "https://open.bigmodel.cn/api-key"
    };

    public static string GetDefaultConfigPath()
    {
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(homeDir, ".nbot", "config.json");
    }

    public static async Task<ConfigurationCheckResult> CheckAsync(string? configPath = null, CancellationToken cancellationToken = default)
    {
        var path = configPath ?? GetDefaultConfigPath();
        var missingFields = new List<string>();
        var warnings = new List<string>();

        if (!File.Exists(path))
        {
            return new ConfigurationCheckResult
            {
                ConfigExists = false,
                HasValidLlm = false,
                HasApiKey = false,
                ConfigPath = path,
                MissingFields = new List<string> { "config file" }
            };
        }

        AgentConfig config;
        try
        {
            config = await ConfigurationLoader.LoadAsync(path, cancellationToken);
        }
        catch (Exception ex)
        {
            return new ConfigurationCheckResult
            {
                ConfigExists = true,
                HasValidLlm = false,
                HasApiKey = false,
                ConfigPath = path,
                MissingFields = new List<string> { $"config file is invalid: {ex.Message}" }
            };
        }

        var hasModel = !string.IsNullOrWhiteSpace(config.Llm.Model);
        var hasProvider = !string.IsNullOrWhiteSpace(config.Llm.Provider);
        var hasApiKey = CheckApiKey(config);

        if (!hasModel)
        {
            missingFields.Add("llm.model");
        }

        if (!hasProvider)
        {
            warnings.Add("LLM provider is not specified, using default routing");
        }

        if (!hasApiKey)
        {
            missingFields.Add("llm.apiKey (or environment variable)");
        }

        return new ConfigurationCheckResult
        {
            ConfigExists = true,
            HasValidLlm = hasModel,
            HasApiKey = hasApiKey,
            ConfigPath = path,
            MissingFields = missingFields,
            Warnings = warnings
        };
    }

    private static bool CheckApiKey(AgentConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.Llm.ApiKey))
        {
            return true;
        }

        var provider = config.Llm.Provider?.ToLowerInvariant() ?? "";
        
        if (ProviderEnvKeys.TryGetValue(provider, out var envKey))
        {
            var envValue = Environment.GetEnvironmentVariable(envKey);
            if (!string.IsNullOrWhiteSpace(envValue))
            {
                return true;
            }
        }

        var genericKeys = new[] { "OPENAI_API_KEY", "ANTHROPIC_API_KEY", "OPENROUTER_API_KEY" };
        foreach (var key in genericKeys)
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return true;
            }
        }

        return false;
    }

    public static string? GetApiKeyFromEnvironment(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            var genericKeys = new[] { "OPENAI_API_KEY", "ANTHROPIC_API_KEY", "OPENROUTER_API_KEY" };
            foreach (var key in genericKeys)
            {
                var value = Environment.GetEnvironmentVariable(key);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
            return null;
        }

        var providerLower = provider.ToLowerInvariant();
        if (ProviderEnvKeys.TryGetValue(providerLower, out var envKey))
        {
            return Environment.GetEnvironmentVariable(envKey);
        }

        return null;
    }
}
