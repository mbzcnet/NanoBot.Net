using System.Reflection;
using System.CommandLine;
using Microsoft.Extensions.Configuration;
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
        services.AddNanoBot(config);

        // Register benchmark services
        services.AddSingleton<IBenchmarkEngine, BenchmarkEngine>();

        var provider = services.BuildServiceProvider();
        NanoBotCommandBase.Initialize(provider, config, configPath);

        var rootCommand = new RootCommand($"{Logo} nbot v{Version} - A lightweight personal AI assistant");

        var commands = GetCommands();
        foreach (var cmd in commands)
        {
            rootCommand.AddCommand(cmd.CreateCommand());
        }

        rootCommand.SetHandler(() =>
        {
            rootCommand.Invoke("--help");
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

    private static IConfiguration BuildConfiguration(string? configPath)
    {
        var configurationBuilder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

        if (!string.IsNullOrEmpty(configPath))
        {
            configurationBuilder.AddJsonFile(configPath, optional: false, reloadOnChange: false);
        }

        return configurationBuilder.Build();
    }

    private static IReadOnlyList<ICliCommand> GetCommands()
    {
        return new ICliCommand[]
        {
            new OnboardCommand(),
            new AgentCommand(),
            new WebUICommand(),
            new GatewayCommand(),
            new StatusCommand(),
            new ConfigCommand(),
            new SessionCommand(),
            new CronCommand(),
            new McpCommand(),
            new ChannelsCommand(),
            new ProviderCommand(),
            new BenchmarkCommand()
        };
    }
}
