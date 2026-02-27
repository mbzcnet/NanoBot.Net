using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NanoBot.Core.Configuration;

public static class ConfigurationLoader
{
    private static readonly Regex EnvVarPattern = new(@"\$\{([^}]+)\}", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions SnakeCaseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private static readonly JsonSerializerOptions DefaultCaseOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<AgentConfig> LoadAsync(string configPath, CancellationToken cancellationToken = default)
    {
        var jsonContent = await File.ReadAllTextAsync(configPath, cancellationToken);
        var processedJson = ReplaceEnvironmentVariables(jsonContent);

        return DeserializeAgentConfig(processedJson);
    }

    public static AgentConfig Load(string configPath)
    {
        var jsonContent = File.ReadAllText(configPath);
        var processedJson = ReplaceEnvironmentVariables(jsonContent);

        return DeserializeAgentConfig(processedJson);
    }

    private static AgentConfig DeserializeAgentConfig(string processedJson)
    {
        var root = JsonNode.Parse(processedJson) as JsonObject;
        if (root == null)
        {
            throw new InvalidOperationException("Failed to parse configuration JSON");
        }

        if (LooksLikeNanobotConfig(root))
        {
            return ConvertFromNanobotConfig(root);
        }

        var preferDefaultCase = LooksLikePascalOrCamelConfig(root);
        var options = preferDefaultCase ? DefaultCaseOptions : SnakeCaseOptions;
        var config = JsonSerializer.Deserialize<AgentConfig>(processedJson, options);
        if (config == null)
        {
            throw new InvalidOperationException("Failed to deserialize configuration");
        }

        if (!preferDefaultCase && ShouldRetryWithDefaultCase(root, config))
        {
            var retry = JsonSerializer.Deserialize<AgentConfig>(processedJson, DefaultCaseOptions);
            if (retry != null)
            {
                return retry;
            }
        }

        return config;
    }

    private static bool LooksLikeNanobotConfig(JsonObject root)
    {
        return root.ContainsKey("agents") || root.ContainsKey("providers");
    }

    private static bool LooksLikePascalOrCamelConfig(JsonObject root)
    {
        if (root.ContainsKey("Name") || root.ContainsKey("Workspace") || root.ContainsKey("Llm") || root.ContainsKey("Channels") || root.ContainsKey("Security") || root.ContainsKey("Memory"))
        {
            return true;
        }

        var llm = GetObject(root, "llm") ?? GetObject(root, "Llm");
        if (llm != null)
        {
            if (llm.ContainsKey("DefaultProfile") || llm.ContainsKey("Profiles") || llm.ContainsKey("ApiKey") || llm.ContainsKey("ApiBase"))
            {
                return true;
            }

            var profiles = GetObject(llm, "profiles") ?? GetObject(llm, "Profiles");
            if (profiles != null)
            {
                var def = GetObject(profiles, "default") ?? GetObject(profiles, "Default");
                if (def != null && (def.ContainsKey("ApiKey") || def.ContainsKey("ApiBase") || def.ContainsKey("MaxTokens") || def.ContainsKey("Temperature") || def.ContainsKey("Model") || def.ContainsKey("Provider")))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ShouldRetryWithDefaultCase(JsonObject root, AgentConfig config)
    {
        var profileName = string.IsNullOrWhiteSpace(config.Llm.DefaultProfile) ? "default" : config.Llm.DefaultProfile;
        var profile = config.Llm.Profiles.GetValueOrDefault(profileName);
        var missingModelOrKey = profile == null || string.IsNullOrWhiteSpace(profile.Model) || string.IsNullOrWhiteSpace(profile.ApiKey);
        if (!missingModelOrKey)
        {
            return false;
        }

        return LooksLikePascalOrCamelConfig(root);
    }

    private static AgentConfig ConvertFromNanobotConfig(JsonObject root)
    {
        var config = new AgentConfig();

        var agents = GetObject(root, "agents");
        var agentDefaults = GetObject(agents, "defaults");

        var workspace = GetString(agentDefaults, "workspace") ?? GetString(agentDefaults, "Workspace");
        if (!string.IsNullOrWhiteSpace(workspace))
        {
            config.Workspace.Path = workspace;
        }

        var model = GetString(agentDefaults, "model") ?? GetString(agentDefaults, "Model");
        if (!string.IsNullOrWhiteSpace(model))
        {
            config.Llm.Profiles["default"].Model = model;
        }

        var temperature = GetDouble(agentDefaults, "temperature") ?? GetDouble(agentDefaults, "Temperature");
        if (temperature != null)
        {
            config.Llm.Profiles["default"].Temperature = temperature.Value;
        }

        var maxTokens = GetInt(agentDefaults, "max_tokens") ?? GetInt(agentDefaults, "maxTokens") ?? GetInt(agentDefaults, "MaxTokens");
        if (maxTokens != null)
        {
            config.Llm.Profiles["default"].MaxTokens = maxTokens.Value;
        }

        var providers = GetObject(root, "providers");
        var providerName = ResolveProviderNameFromNanobotConfig(providers, model);
        if (!string.IsNullOrWhiteSpace(providerName))
        {
            config.Llm.Profiles["default"].Provider = providerName;
            var provider = GetObject(providers, providerName);
            if (provider != null)
            {
                var apiKey = GetString(provider, "api_key") ?? GetString(provider, "apiKey") ?? GetString(provider, "ApiKey");
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    config.Llm.Profiles["default"].ApiKey = apiKey;
                }

                var apiBase = GetString(provider, "api_base") ?? GetString(provider, "apiBase") ?? GetString(provider, "ApiBase");
                if (!string.IsNullOrWhiteSpace(apiBase))
                {
                    config.Llm.Profiles["default"].ApiBase = apiBase;
                }
            }
        }

        return config;
    }

    private static string? ResolveProviderNameFromNanobotConfig(JsonObject? providers, string? model)
    {
        if (providers == null)
        {
            return null;
        }

        var modelPrefix = (model ?? string.Empty).Split('/', 2)[0].ToLowerInvariant();
        var modelPrefixNormalized = modelPrefix.Replace("-", "_");

        // 1) Prefer explicit prefix match: providers.<prefix>
        if (!string.IsNullOrWhiteSpace(modelPrefix))
        {
            foreach (var kv in providers)
            {
                var name = kv.Key;
                var normalized = name.ToLowerInvariant().Replace("-", "_");
                if (normalized == modelPrefixNormalized)
                {
                    return name;
                }
            }
        }

        // 2) Fallback: first provider that has apiKey configured (snake_case or camelCase)
        foreach (var kv in providers)
        {
            var p = kv.Value as JsonObject;
            if (p == null)
            {
                continue;
            }

            var apiKey = GetString(p, "api_key") ?? GetString(p, "apiKey") ?? GetString(p, "ApiKey");
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                return kv.Key;
            }
        }

        // 3) Last resort: just return first provider entry
        foreach (var kv in providers)
        {
            return kv.Key;
        }

        return null;
    }

    private static JsonObject? GetObject(JsonObject? obj, string key)
    {
        if (obj == null)
        {
            return null;
        }

        var node = obj[key];
        return node as JsonObject;
    }

    private static string? GetString(JsonObject? obj, string key)
    {
        if (obj == null)
        {
            return null;
        }

        var node = obj[key];
        return node?.GetValue<string?>();
    }

    private static int? GetInt(JsonObject? obj, string key)
    {
        if (obj == null)
        {
            return null;
        }

        var node = obj[key];
        if (node == null)
        {
            return null;
        }

        if (node is JsonValue v)
        {
            if (v.TryGetValue<int>(out var i))
            {
                return i;
            }

            if (v.TryGetValue<string>(out var s) && int.TryParse(s, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static double? GetDouble(JsonObject? obj, string key)
    {
        if (obj == null)
        {
            return null;
        }

        var node = obj[key];
        if (node == null)
        {
            return null;
        }

        if (node is JsonValue v)
        {
            if (v.TryGetValue<double>(out var d))
            {
                return d;
            }

            if (v.TryGetValue<string>(out var s) && double.TryParse(s, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    public static string ReplaceEnvironmentVariables(string content)
    {
        return EnvVarPattern.Replace(content, match =>
        {
            var varName = match.Groups[1].Value;
            var envValue = Environment.GetEnvironmentVariable(varName);
            return envValue ?? string.Empty;
        });
    }

    public static async Task<AgentConfig> LoadWithDefaultsAsync(
        string? configPath = null, 
        CancellationToken cancellationToken = default)
    {
        var resolved = ConfigurationChecker.ResolveExistingConfigPath(configPath) ?? configPath;

        if (resolved != null && File.Exists(resolved))
        {
            return await LoadAsync(resolved, cancellationToken);
        }

        return new AgentConfig();
    }

    public static async Task SaveAsync(string configPath, AgentConfig config, CancellationToken cancellationToken = default)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };
        
        var directory = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        var jsonContent = JsonSerializer.Serialize(config, options);
        await File.WriteAllTextAsync(configPath, jsonContent, cancellationToken);
    }
}
