using System.CommandLine;
using System.Text.Json;
using NanoBot.Core.Configuration;

namespace NanoBot.Cli.Commands;

public class StatusCommand : ICliCommand
{
    public string Name => "status";
    public string Description => "Show Agent status";

    public Command CreateCommand()
    {
        var jsonOption = new Option<bool>(
            name: "--json",
            description: "Output in JSON format",
            getDefaultValue: () => false
        );

        var configOption = new Option<string?>(
            name: "--config",
            description: "Configuration file path"
        );
        configOption.AddAlias("-c");

        var command = new Command(Name, Description)
        {
            jsonOption,
            configOption
        };

        command.SetHandler(async (context) =>
        {
            var json = context.ParseResult.GetValueForOption(jsonOption);
            var configPath = context.ParseResult.GetValueForOption(configOption);
            var cancellationToken = context.GetCancellationToken();
            await ExecuteStatusAsync(json, configPath, cancellationToken);
        });

        return command;
    }

    private async Task ExecuteStatusAsync(
        bool outputJson,
        string? configPath,
        CancellationToken cancellationToken)
    {
        var config = await ConfigurationLoader.LoadWithDefaultsAsync(configPath, cancellationToken);
        var workspacePath = config.Workspace.GetResolvedPath();
        var configFilePath = GetConfigPath(configPath);

        if (outputJson)
        {
            var status = new
            {
                config_path = configFilePath,
                config_exists = File.Exists(configFilePath),
                workspace_path = workspacePath,
                workspace_exists = Directory.Exists(workspacePath),
                agent_name = config.Name,
                llm_provider = config.Llm.Provider,
                llm_model = config.Llm.Model,
                channels = new
                {
                    telegram = config.Channels.Telegram?.Enabled ?? false,
                    discord = config.Channels.Discord?.Enabled ?? false,
                    slack = config.Channels.Slack?.Enabled ?? false,
                    whatsapp = config.Channels.WhatsApp?.Enabled ?? false
                }
            };

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            };

            Console.WriteLine(JsonSerializer.Serialize(status, jsonOptions));
        }
        else
        {
            Console.WriteLine("🐈 nbot Status\n");

            Console.WriteLine($"Config: {configFilePath} {(File.Exists(configFilePath) ? "✓" : "✗")}");
            Console.WriteLine($"Workspace: {workspacePath} {(Directory.Exists(workspacePath) ? "✓" : "✗")}");

            if (File.Exists(configFilePath))
            {
                Console.WriteLine($"\nAgent Name: {config.Name}");
                Console.WriteLine($"LLM Provider: {config.Llm.Provider}");
                Console.WriteLine($"LLM Model: {config.Llm.Model}");

                Console.WriteLine("\nChannels:");

                PrintChannelStatus("Telegram", config.Channels.Telegram?.Enabled ?? false, 
                    config.Channels.Telegram?.Token != null ? "configured" : "not configured");

                PrintChannelStatus("Discord", config.Channels.Discord?.Enabled ?? false,
                    config.Channels.Discord?.Token != null ? "configured" : "not configured");

                PrintChannelStatus("Slack", config.Channels.Slack?.Enabled ?? false,
                    config.Channels.Slack?.BotToken != null ? "configured" : "not configured");

                PrintChannelStatus("WhatsApp", config.Channels.WhatsApp?.Enabled ?? false,
                    config.Channels.WhatsApp?.BridgeUrl ?? "not configured");
            }
        }
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

    private static void PrintChannelStatus(string name, bool enabled, string configStatus)
    {
        var status = enabled ? "✓" : "✗";
        var enabledText = enabled ? "enabled" : "disabled";
        Console.WriteLine($"  {name}: {status} ({enabledText}, {configStatus})");
    }
}
