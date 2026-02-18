using System.CommandLine;
using System.Text.Json;
using NanoBot.Core.Configuration;

namespace NanoBot.Cli.Commands;

public class ChannelsCommand : ICliCommand
{
    public string Name => "channels";
    public string Description => "Manage channels";

    public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var statusCommand = new Command("status", "Show channel status");
        statusCommand.SetHandler(async () =>
        {
            await ShowChannelStatusAsync(cancellationToken);
        });

        var loginCommand = new Command("login", "Link device via QR code");
        loginCommand.SetHandler(async () =>
        {
            await LoginAsync(cancellationToken);
        });

        var command = new Command(Name, Description);
        command.AddCommand(statusCommand);
        command.AddCommand(loginCommand);

        return await command.InvokeAsync(args);
    }

    private static async Task ShowChannelStatusAsync(CancellationToken cancellationToken)
    {
        var config = await ConfigurationLoader.LoadWithDefaultsAsync(null, cancellationToken);

        Console.WriteLine("Channel Status\n");

        Console.WriteLine($"{"Channel",-15} {"Enabled",-10} {"Configuration"}");
        Console.WriteLine(new string('-', 60));

        PrintChannelRow("WhatsApp", config.Channels.WhatsApp?.Enabled ?? false,
            config.Channels.WhatsApp?.BridgeUrl ?? "not configured");

        PrintChannelRow("Discord", config.Channels.Discord?.Enabled ?? false,
            !string.IsNullOrEmpty(config.Channels.Discord?.Token) ? "configured" : "not configured");

        PrintChannelRow("Slack", config.Channels.Slack?.Enabled ?? false,
            !string.IsNullOrEmpty(config.Channels.Slack?.AppToken) ? "socket mode" : "not configured");

        PrintChannelRow("Telegram", config.Channels.Telegram?.Enabled ?? false,
            !string.IsNullOrEmpty(config.Channels.Telegram?.Token) ? "configured" : "not configured");

        PrintChannelRow("Feishu", config.Channels.Feishu?.Enabled ?? false,
            !string.IsNullOrEmpty(config.Channels.Feishu?.AppId) 
                ? $"app_id: {config.Channels.Feishu.AppId[..Math.Min(10, config.Channels.Feishu.AppId.Length)]}..." 
                : "not configured");

        PrintChannelRow("DingTalk", config.Channels.DingTalk?.Enabled ?? false,
            !string.IsNullOrEmpty(config.Channels.DingTalk?.ClientId) ? "configured" : "not configured");

        PrintChannelRow("Mochat", config.Channels.Mochat?.Enabled ?? false,
            config.Channels.Mochat?.BaseUrl ?? "not configured");

        PrintChannelRow("QQ", config.Channels.QQ?.Enabled ?? false,
            !string.IsNullOrEmpty(config.Channels.QQ?.AppId) ? "configured" : "not configured");

        PrintChannelRow("Email", config.Channels.Email?.Enabled ?? false,
            config.Channels.Email?.SmtpHost ?? "not configured");
    }

    private static void PrintChannelRow(string name, bool enabled, string config)
    {
        var enabledStr = enabled ? "✓" : "✗";
        Console.WriteLine($"{name,-15} {enabledStr,-10} {config}");
    }

    private static Task LoginAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("🐈 Starting bridge...");
        Console.WriteLine("Scan the QR code to connect.\n");
        Console.WriteLine("Note: This feature requires Node.js and the WhatsApp bridge.");
        Console.WriteLine("Please refer to: https://github.com/HKUDS/nanobot#-chat-apps");
        Console.WriteLine("\nFor now, manually configure WhatsApp bridge or use other channels.");

        return Task.CompletedTask;
    }
}
