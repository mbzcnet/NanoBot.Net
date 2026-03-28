using System.Reflection;
using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NanoBot.Cli.Commands;
using NanoBot.Cli.Extensions;
using NanoBot.Core.Benchmark;
using NanoBot.Core.Configuration;
using NanoBot.Providers.Benchmark;

namespace NanoBot.Cli;

public static class Program
{
    private const string Logo = "🐈";
    private static readonly string Version = typeof(Program)
        .Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion ?? typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 1 && (args[0] == "--version" || args[0] == "-v"))
        {
            Console.WriteLine($"{Logo} nbot v{Version}");
            return 0;
        }

        var configPath = GetConfigPath(args);
        var config = await ConfigurationLoader.LoadWithDefaultsAsync(configPath);

        var services = new ServiceCollection();
        NanoBot.Agent.ServiceCollectionExtensions.AddNanoBot(services, config);

        // Register benchmark services
        services.AddSingleton<IBenchmarkEngine, BenchmarkEngine>();

        var provider = services.BuildServiceProvider();
        var context = new CliCommandContext(config, configPath ?? "", provider);

        var rootCommand = new RootCommand($"{Logo} nbot v{Version} - A lightweight personal AI assistant");

        var commands = GetCommands(context);
        foreach (var cmd in commands)
        {
            rootCommand.AddCommand(cmd.CreateCommand());
        }

        rootCommand.SetHandler(async () =>
        {
            // Default to agent mode when no command is specified
            await rootCommand.InvokeAsync("agent");
        });

        return await rootCommand.InvokeAsync(args);
    }

    private static string? GetConfigPath(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--config" || args[i] == "-c")
            {
                return args[i + 1];
            }
        }
        return null;
    }

    private static IReadOnlyList<ICliCommand> GetCommands(CliCommandContext context)
    {
        return new ICliCommand[]
        {
            new OnboardCommand(),
            new AgentCommand(),
            new WebUICommand(),
            new GatewayCommand(),
            new StatusCommand(context),
            new ConfigCommand(),
            new SessionCommand(),
            new CronCommand(),
            new McpCommand(),
            new ChannelsCommand(),
            new ProviderCommand(),
            new BenchmarkCommand(context)
        };
    }
}
