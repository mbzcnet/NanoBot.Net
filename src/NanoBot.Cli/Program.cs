using System.Reflection;
using System.CommandLine;
using NanoBot.Cli.Commands;

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

    private static IReadOnlyList<ICliCommand> GetCommands()
    {
        return new ICliCommand[]
        {
            new OnboardCommand(),
            new AgentCommand(),
            new GatewayCommand(),
            new StatusCommand(),
            new ConfigCommand(),
            new SessionCommand(),
            new CronCommand(),
            new McpCommand(),
            new ChannelsCommand(),
            new ProviderCommand()
        };
    }
}
