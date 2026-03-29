using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NanoBot.Channels.Implementations.WeiXin;
using NanoBot.Core.Bus;
using NanoBot.Core.Configuration;

namespace NanoBot.Cli.Commands;

public class ChannelsCommand : ICliCommand
{
    public string Name => "channels";
    public string Description => "Manage channels";

    public Command CreateCommand()
    {
        var statusCommand = new Command("status", "Show channel status");
        statusCommand.SetHandler(async (context) =>
        {
            var cancellationToken = context.GetCancellationToken();
            await ShowChannelStatusAsync(cancellationToken);
        });

        var loginCommand = new Command("login", "Link device via QR code");
        loginCommand.SetHandler(async (context) =>
        {
            var cancellationToken = context.GetCancellationToken();
            await LoginAsync(cancellationToken);
        });

        var command = new Command(Name, Description);
        command.AddCommand(statusCommand);
        command.AddCommand(loginCommand);

        return command;
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

        PrintChannelRow("WeiXin", config.Channels.WeiXin?.Enabled ?? false,
            !string.IsNullOrEmpty(config.Channels.WeiXin?.Token) ? "authenticated" : "not configured");

        PrintChannelRow("Email", config.Channels.Email?.Enabled ?? false,
            config.Channels.Email?.SmtpHost ?? "not configured");
    }

    private static void PrintChannelRow(string name, bool enabled, string config)
    {
        var enabledStr = enabled ? "✓" : "✗";
        Console.WriteLine($"{name,-15} {enabledStr,-10} {config}");
    }

    private static async Task LoginAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("Select channel to link via QR code:");
        Console.WriteLine("  1. WhatsApp");
        Console.WriteLine("  2. WeiXin (微信)");
        Console.Write("\nEnter choice (1-2): ");

        var choice = Console.ReadLine()?.Trim();
        if (choice == "1")
        {
            Console.WriteLine("\n🐈 Starting WhatsApp bridge...");
            Console.WriteLine("Scan the QR code to connect.\n");
            Console.WriteLine("Note: This feature requires Node.js and the WhatsApp bridge.");
            Console.WriteLine("Please refer to: https://github.com/HKUDS/nanobot#-chat-apps");
            Console.WriteLine("\nFor now, manually configure WhatsApp bridge or use other channels.");
        }
        else if (choice == "2")
        {
            await WeiXinLoginAsync(cancellationToken);
        }
        else
        {
            Console.WriteLine("Invalid choice.");
        }
    }

    private static async Task WeiXinLoginAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("\n=== WeiXin QR Login ===");
        Console.WriteLine("This will open a web-based QR code for you to scan with WeChat.\n");

        var configPath = Environment.GetEnvironmentVariable("NBOT_CONFIG_PATH")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nanobot", "config.json");

        var config = await ConfigurationLoader.LoadWithDefaultsAsync(configPath, cancellationToken);

        var weixinConfig = config.Channels.WeiXin ?? new WeiXinConfig();
        var stateDir = string.IsNullOrEmpty(weixinConfig.StateDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nanobot", "weixin")
            : weixinConfig.StateDir;
        var tokenPath = Path.Combine(stateDir, "account.json");

        if (File.Exists(tokenPath))
        {
            Console.WriteLine("WeiXin account already authenticated.");
            Console.WriteLine($"  State: {tokenPath}");
            Console.Write("\nRe-authenticate? (y/N): ");
            if (!Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) ?? true)
                return;
        }

        Console.WriteLine("\nStarting WeiXin QR login...");
        Console.WriteLine("(Make sure WeChat is logged in on this device before scanning)\n");

        // Create a minimal DI container for the channel
        var services = new ServiceCollection();
        services.AddSingleton(config);
        NanoBot.Agent.ServiceCollectionExtensions.AddNanoBotChannels(services, config.Channels);
        var provider = services.BuildServiceProvider();

        // Resolve the channel directly and call QRLoginAsync
        // We can't resolve WeiXinChannel directly since it's not registered as a service.
        // Instead, we use the ChannelFactory approach.
        var bus = provider.GetRequiredService<NanoBot.Core.Bus.IMessageBus>();
        var loggerFactory = provider.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<NanoBot.Channels.Implementations.WeiXin.WeiXinChannel>();
        var channel = new NanoBot.Channels.Implementations.WeiXin.WeiXinChannel(weixinConfig, bus, logger);

        try
        {
            var success = await channel.QRLoginAsync(cancellationToken);
            if (success)
            {
                weixinConfig.Token = ""; // Token is stored in state file, not config
                config.Channels.WeiXin = weixinConfig;
                await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
                Console.WriteLine("\n WeiXin login successful!");
                Console.WriteLine($"  Token stored at: {tokenPath}");
            }
            else
            {
                Console.WriteLine("\n WeiXin login failed or cancelled.");
            }
        }
        finally
        {
            await provider.DisposeAsync();
        }
    }
}
