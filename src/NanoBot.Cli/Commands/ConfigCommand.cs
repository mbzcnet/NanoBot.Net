using System.CommandLine;
using System.Text.Json;
using NanoBot.Core.Configuration;
using NanoBot.Cli.Services;

namespace NanoBot.Cli.Commands;

public class ConfigCommand : ICliCommand
{
    public string Name => "config";
    public string Description => "Configuration management";

    public Command CreateCommand()
    {
        var listOption = new Option<bool>(
            name: "--list",
            description: "List all configuration",
            getDefaultValue: () => false
        );
        listOption.AddAlias("-l");

        var getOption = new Option<string?>(
            name: "--get",
            description: "Get specific configuration key"
        );
        getOption.AddAlias("-g");

        var setOption = new Option<string[]?>(
            name: "--set",
            description: "Set configuration key=value"
        );
        setOption.AddAlias("-s");

        var configOption = new Option<string?>(
            name: "--config",
            description: "Configuration file path"
        );
        configOption.AddAlias("-c");

        var interactiveOption = new Option<bool>(
            name: "--interactive",
            description: "Interactive LLM profile management",
            getDefaultValue: () => false
        );
        interactiveOption.AddAlias("-i");

        var command = new Command(Name, Description)
        {
            listOption,
            getOption,
            setOption,
            configOption,
            interactiveOption
        };

        command.SetHandler(async (context) =>
        {
            var list = context.ParseResult.GetValueForOption(listOption);
            var get = context.ParseResult.GetValueForOption(getOption);
            var set = context.ParseResult.GetValueForOption(setOption);
            var configPath = context.ParseResult.GetValueForOption(configOption);
            var interactive = context.ParseResult.GetValueForOption(interactiveOption);
            var cancellationToken = context.GetCancellationToken();
            await ExecuteConfigAsync(list, get, set, configPath, interactive, cancellationToken);
        });

        return command;
    }

    private async Task ExecuteConfigAsync(
        bool list,
        string? get,
        string[]? set,
        string? configPath,
        bool interactive,
        CancellationToken cancellationToken)
    {
        var configFilePath = GetConfigPath(configPath);

        if (interactive)
        {
            await RunInteractiveLlmManagementAsync(configFilePath, cancellationToken);
            return;
        }

        if (set != null && set.Length > 0)
        {
            await SetConfigValuesAsync(configFilePath, set, cancellationToken);
            return;
        }

        if (!string.IsNullOrEmpty(get))
        {
            await GetConfigValueAsync(configFilePath, get, cancellationToken);
            return;
        }

        if (list)
        {
            await ListConfigAsync(configFilePath, cancellationToken);
            return;
        }

        await ListConfigAsync(configFilePath, cancellationToken);
    }

    private async Task RunInteractiveLlmManagementAsync(string configPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(configPath))
        {
            Console.WriteLine($"Config file not found: {configPath}");
            Console.WriteLine("Run 'nbot onboard' to create a configuration.");
            return;
        }

        var config = await ConfigurationLoader.LoadAsync(configPath, cancellationToken);
        var service = new LlmProfileConfigService();
        await service.ManageProfilesInteractiveAsync(config, configPath, cancellationToken);
    }

    private static string GetConfigPath(string? configPath)
    {
        if (!string.IsNullOrEmpty(configPath))
        {
            return Path.GetFullPath(configPath);
        }

        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(homeDir, ".nbot", "config.json");
    }

    private static async Task ListConfigAsync(string configPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(configPath))
        {
            Console.WriteLine($"Config file not found: {configPath}");
            Console.WriteLine("Run 'nbot onboard' to create a configuration.");
            return;
        }

        var config = await ConfigurationLoader.LoadAsync(configPath, cancellationToken);

        Console.WriteLine($"Configuration: {configPath}\n");

        Console.WriteLine($"Name: {config.Name}");
        Console.WriteLine($"Workspace: {config.Workspace.Path}");

        Console.WriteLine("\nLLM:");
        Console.WriteLine($"  Default Profile: {config.Llm.DefaultProfile}");
        
        foreach (var (profileName, profile) in config.Llm.Profiles)
        {
            Console.WriteLine($"\n  Profile [{profileName}]:");
            Console.WriteLine($"    Provider: {profile.Provider}");
            Console.WriteLine($"    Model: {profile.Model}");
            if (!string.IsNullOrEmpty(profile.ApiKey))
            {
                Console.WriteLine($"    API Key: {MaskApiKey(profile.ApiKey)}");
            }
            if (!string.IsNullOrEmpty(profile.ApiBase))
            {
                Console.WriteLine($"    API Base: {profile.ApiBase}");
            }
            Console.WriteLine($"    Temperature: {profile.Temperature}");
            Console.WriteLine($"    MaxTokens: {profile.MaxTokens}");
        }

        Console.WriteLine("\nChannels:");
        PrintChannelConfig("Telegram", config.Channels.Telegram);
        PrintChannelConfig("Discord", config.Channels.Discord);
        PrintChannelConfig("Slack", config.Channels.Slack);
        PrintChannelConfig("WhatsApp", config.Channels.WhatsApp);
    }

    private static async Task GetConfigValueAsync(string configPath, string key, CancellationToken cancellationToken)
    {
        if (!File.Exists(configPath))
        {
            Console.WriteLine($"Config file not found: {configPath}");
            return;
        }

        var config = await ConfigurationLoader.LoadAsync(configPath, cancellationToken);
        var value = GetConfigValueByKey(config, key);

        if (value != null)
        {
            Console.WriteLine(value);
        }
        else
        {
            Console.WriteLine($"Key not found: {key}");
        }
    }

    private static async Task SetConfigValuesAsync(string configPath, string[] setValues, CancellationToken cancellationToken)
    {
        AgentConfig config;

        if (File.Exists(configPath))
        {
            config = await ConfigurationLoader.LoadAsync(configPath, cancellationToken);
        }
        else
        {
            config = new AgentConfig();
        }

        foreach (var setValue in setValues)
        {
            var parts = setValue.Split('=', 2);
            if (parts.Length != 2)
            {
                Console.WriteLine($"Invalid format: {setValue}. Use key=value");
                continue;
            }

            var key = parts[0].Trim();
            var value = parts[1].Trim();

            SetConfigValueByKey(config, key, value);
            Console.WriteLine($"Set {key} = {value}");
        }

        await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
        Console.WriteLine($"\nConfiguration saved to {configPath}");
    }

    private static string? GetConfigValueByKey(AgentConfig config, string key)
    {
        if (key.StartsWith("llm.profiles.", StringComparison.OrdinalIgnoreCase))
        {
            var parts = key.Split('.', 4);
            if (parts.Length >= 4)
            {
                var profileName = parts[2];
                var field = parts[3].ToLowerInvariant();
                
                if (config.Llm.Profiles.TryGetValue(profileName, out var profile))
                {
                    return field switch
                    {
                        "provider" => profile.Provider,
                        "model" => profile.Model,
                        "apikey" => profile.ApiKey,
                        "apibase" => profile.ApiBase,
                        "temperature" => profile.Temperature.ToString(),
                        "maxtokens" => profile.MaxTokens.ToString(),
                        _ => null
                    };
                }
            }
            return null;
        }

        if (key.StartsWith("llm.", StringComparison.OrdinalIgnoreCase))
        {
            var field = key[4..].ToLowerInvariant();
            return field switch
            {
                "defaultprofile" => config.Llm.DefaultProfile,
                "provider" => GetDefaultProfile(config)?.Provider,
                "model" => GetDefaultProfile(config)?.Model,
                "apikey" => GetDefaultProfile(config)?.ApiKey,
                "apibase" => GetDefaultProfile(config)?.ApiBase,
                "temperature" => GetDefaultProfile(config)?.Temperature.ToString(),
                "maxtokens" => GetDefaultProfile(config)?.MaxTokens.ToString(),
                _ => null
            };
        }

        return key.ToLowerInvariant() switch
        {
            "name" => config.Name,
            "workspace.path" => config.Workspace.Path,
            _ => null
        };
    }

    private static LlmProfile? GetDefaultProfile(AgentConfig config)
    {
        var profileName = string.IsNullOrEmpty(config.Llm.DefaultProfile) ? "default" : config.Llm.DefaultProfile;
        return config.Llm.Profiles.GetValueOrDefault(profileName);
    }

    private static void SetConfigValueByKey(AgentConfig config, string key, string value)
    {
        if (key.StartsWith("llm.profiles.", StringComparison.OrdinalIgnoreCase))
        {
            var parts = key.Split('.', 4);
            if (parts.Length >= 4)
            {
                var profileName = parts[2];
                var field = parts[3].ToLowerInvariant();
                
                if (!config.Llm.Profiles.ContainsKey(profileName))
                {
                    config.Llm.Profiles[profileName] = new LlmProfile { Name = profileName };
                }
                
                var profile = config.Llm.Profiles[profileName];
                switch (field)
                {
                    case "provider":
                        profile.Provider = value;
                        break;
                    case "model":
                        profile.Model = value;
                        break;
                    case "apikey":
                        profile.ApiKey = value;
                        break;
                    case "apibase":
                        profile.ApiBase = value;
                        break;
                    case "temperature":
                        if (float.TryParse(value, out var temp))
                            profile.Temperature = temp;
                        break;
                    case "maxtokens":
                        if (int.TryParse(value, out var maxTokens))
                            profile.MaxTokens = maxTokens;
                        break;
                }
            }
            return;
        }

        switch (key.ToLowerInvariant())
        {
            case "name":
                config.Name = value;
                break;
            case "workspace.path":
                config.Workspace.Path = value;
                break;
            case "llm.defaultprofile":
                config.Llm.DefaultProfile = value;
                break;
            case "llm.provider":
                EnsureDefaultProfile(config).Provider = value;
                break;
            case "llm.model":
                EnsureDefaultProfile(config).Model = value;
                break;
            case "llm.apikey":
                EnsureDefaultProfile(config).ApiKey = value;
                break;
            case "llm.apibase":
                EnsureDefaultProfile(config).ApiBase = value;
                break;
            case "llm.temperature":
                if (float.TryParse(value, out var temp))
                    EnsureDefaultProfile(config).Temperature = temp;
                break;
            case "llm.maxtokens":
                if (int.TryParse(value, out var maxTokens))
                    EnsureDefaultProfile(config).MaxTokens = maxTokens;
                break;
        }
    }

    private static LlmProfile EnsureDefaultProfile(AgentConfig config)
    {
        var profileName = string.IsNullOrEmpty(config.Llm.DefaultProfile) ? "default" : config.Llm.DefaultProfile;
        if (!config.Llm.Profiles.ContainsKey(profileName))
        {
            config.Llm.Profiles[profileName] = new LlmProfile { Name = profileName };
        }
        return config.Llm.Profiles[profileName];
    }

    private static string MaskApiKey(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey) || apiKey.Length < 8)
        {
            return "***";
        }
        return $"{apiKey[..4]}...{apiKey[^4..]}";
    }

    private static void PrintChannelConfig<T>(string name, T? channelConfig) where T : class
    {
        if (channelConfig == null)
        {
            Console.WriteLine($"  {name}: not configured");
            return;
        }

        var enabledProp = typeof(T).GetProperty("Enabled");
        var enabled = enabledProp?.GetValue(channelConfig) as bool? ?? false;

        Console.WriteLine($"  {name}: {(enabled ? "enabled" : "disabled")}");
    }
}
