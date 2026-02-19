using System.CommandLine;
using System.Text.Json;
using NanoBot.Core.Configuration;

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

        var command = new Command(Name, Description)
        {
            listOption,
            getOption,
            setOption,
            configOption
        };

        command.SetHandler(async (context) =>
        {
            var list = context.ParseResult.GetValueForOption(listOption);
            var get = context.ParseResult.GetValueForOption(getOption);
            var set = context.ParseResult.GetValueForOption(setOption);
            var configPath = context.ParseResult.GetValueForOption(configOption);
            var cancellationToken = context.GetCancellationToken();
            await ExecuteConfigAsync(list, get, set, configPath, cancellationToken);
        });

        return command;
    }

    private async Task ExecuteConfigAsync(
        bool list,
        string? get,
        string[]? set,
        string? configPath,
        CancellationToken cancellationToken)
    {
        var configFilePath = GetConfigPath(configPath);

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
        Console.WriteLine($"  Provider: {config.Llm.Provider}");
        Console.WriteLine($"  Model: {config.Llm.Model}");
        if (!string.IsNullOrEmpty(config.Llm.ApiKey))
        {
            Console.WriteLine($"  API Key: {MaskApiKey(config.Llm.ApiKey)}");
        }
        if (!string.IsNullOrEmpty(config.Llm.ApiBase))
        {
            Console.WriteLine($"  API Base: {config.Llm.ApiBase}");
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
        return key.ToLowerInvariant() switch
        {
            "name" => config.Name,
            "workspace.path" => config.Workspace.Path,
            "llm.provider" => config.Llm.Provider,
            "llm.model" => config.Llm.Model,
            "llm.apikey" => config.Llm.ApiKey,
            "llm.apibase" => config.Llm.ApiBase,
            "llm.temperature" => config.Llm.Temperature.ToString(),
            "llm.maxtokens" => config.Llm.MaxTokens.ToString(),
            _ => null
        };
    }

    private static void SetConfigValueByKey(AgentConfig config, string key, string value)
    {
        switch (key.ToLowerInvariant())
        {
            case "name":
                config.Name = value;
                break;
            case "workspace.path":
                config.Workspace.Path = value;
                break;
            case "llm.provider":
                config.Llm.Provider = value;
                break;
            case "llm.model":
                config.Llm.Model = value;
                break;
            case "llm.apikey":
                config.Llm.ApiKey = value;
                break;
            case "llm.apibase":
                config.Llm.ApiBase = value;
                break;
            case "llm.temperature":
                if (float.TryParse(value, out var temp))
                {
                    config.Llm.Temperature = temp;
                }
                break;
            case "llm.maxtokens":
                if (int.TryParse(value, out var maxTokens))
                {
                    config.Llm.MaxTokens = maxTokens;
                }
                break;
        }
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
