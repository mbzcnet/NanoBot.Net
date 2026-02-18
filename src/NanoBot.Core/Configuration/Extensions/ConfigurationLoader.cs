using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NanoBot.Core.Configuration;

public static class ConfigurationLoader
{
    private static readonly Regex EnvVarPattern = new(@"\$\{([^}]+)\}", RegexOptions.Compiled);

    public static async Task<AgentConfig> LoadAsync(string configPath, CancellationToken cancellationToken = default)
    {
        var jsonContent = await File.ReadAllTextAsync(configPath, cancellationToken);
        var processedJson = ReplaceEnvironmentVariables(jsonContent);
        
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };
        
        var config = JsonSerializer.Deserialize<AgentConfig>(processedJson, options);
        return config ?? throw new InvalidOperationException("Failed to deserialize configuration");
    }

    public static AgentConfig Load(string configPath)
    {
        var jsonContent = File.ReadAllText(configPath);
        var processedJson = ReplaceEnvironmentVariables(jsonContent);
        
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };
        
        var config = JsonSerializer.Deserialize<AgentConfig>(processedJson, options);
        return config ?? throw new InvalidOperationException("Failed to deserialize configuration");
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
        if (configPath != null && File.Exists(configPath))
        {
            return await LoadAsync(configPath, cancellationToken);
        }

        var defaultPaths = new[]
        {
            "config.json",
            "agent.json",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nanobot", "config.json")
        };

        foreach (var path in defaultPaths)
        {
            if (File.Exists(path))
            {
                return await LoadAsync(path, cancellationToken);
            }
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
