using System.CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NanoBot.Agent;
using NanoBot.Cli.Extensions;
using NanoBot.Core.Channels;
using NanoBot.Core.Configuration;
using NanoBot.Core.Cron;
using NanoBot.Core.Heartbeat;
using NanoBot.Core.Workspace;

namespace NanoBot.Cli.Commands;

public class GatewayCommand : ICliCommand
{
    public string Name => "gateway";
    public string Description => "Start Gateway service mode";

    public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var portOption = new Option<int>(
            name: "--port",
            description: "Gateway port",
            getDefaultValue: () => 18790
        );
        portOption.AddAlias("-p");

        var configOption = new Option<string?>(
            name: "--config",
            description: "Configuration file path"
        );
        configOption.AddAlias("-c");

        var verboseOption = new Option<bool>(
            name: "--verbose",
            description: "Verbose output",
            getDefaultValue: () => false
        );
        verboseOption.AddAlias("-v");

        var command = new Command(Name, Description)
        {
            portOption,
            configOption,
            verboseOption
        };

        command.SetHandler(async (port, configPath, verbose) =>
        {
            await ExecuteGatewayAsync(port, configPath, verbose, cancellationToken);
        }, portOption, configOption, verboseOption);

        return await command.InvokeAsync(args);
    }

    private async Task ExecuteGatewayAsync(
        int port,
        string? configPath,
        bool verbose,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"🐈 Starting nbot gateway on port {port}...\n");

        var config = await ConfigurationLoader.LoadWithDefaultsAsync(configPath, cancellationToken);

        var services = new ServiceCollection();
        var configuration = BuildConfiguration(configPath);

        services.AddNanoBot(configuration);

        if (verbose)
        {
            services.Configure<LoggerFilterOptions>(options =>
            {
                options.MinLevel = LogLevel.Debug;
            });
        }

        var serviceProvider = services.BuildServiceProvider();

        var workspace = serviceProvider.GetRequiredService<IWorkspaceManager>();
        await workspace.InitializeAsync(cancellationToken);

        var runtime = serviceProvider.GetRequiredService<IAgentRuntime>();
        var channelManager = serviceProvider.GetRequiredService<IChannelManager>();
        var cronService = serviceProvider.GetRequiredService<ICronService>();
        var heartbeatService = serviceProvider.GetRequiredService<IHeartbeatService>();

        var enabledChannels = channelManager.EnabledChannels;
        if (enabledChannels.Count > 0)
        {
            Console.WriteLine($"✓ Channels enabled: {string.Join(", ", enabledChannels)}");
        }
        else
        {
            Console.WriteLine("Warning: No channels enabled");
        }

        var cronStatus = cronService.GetStatus();
        if (cronStatus.TotalJobs > 0)
        {
            Console.WriteLine($"✓ Cron: {cronStatus.TotalJobs} scheduled jobs");
        }

        Console.WriteLine("✓ Heartbeat: every 30m");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            Console.WriteLine("\nShutting down...");
        };

        try
        {
            var agentTask = runtime.RunAsync(cts.Token);
            var channelsTask = channelManager.StartAllAsync(cts.Token);

            await Task.WhenAll(agentTask, channelsTask);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            Console.WriteLine("Stopping services...");

            runtime.Stop();
            await heartbeatService.StopAsync();
            await cronService.StopAsync();
            await channelManager.StopAllAsync();

            if (runtime is IDisposable disposable)
            {
                disposable.Dispose();
            }

            Console.WriteLine("Gateway stopped.");
        }
    }

    private static IConfiguration BuildConfiguration(string? configPath)
    {
        var builder = new ConfigurationBuilder();

        if (!string.IsNullOrEmpty(configPath) && File.Exists(configPath))
        {
            builder.AddJsonFile(configPath, optional: false);
        }
        else
        {
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var defaultConfigPath = Path.Combine(homeDir, ".nbot", "config.json");
            if (File.Exists(defaultConfigPath))
            {
                builder.AddJsonFile(defaultConfigPath, optional: false);
            }
        }

        builder.AddEnvironmentVariables();
        return builder.Build();
    }
}
