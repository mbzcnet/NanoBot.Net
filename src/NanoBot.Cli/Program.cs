using System.CommandLine;
using NanoBot.Cli.Commands;

namespace NanoBot.Cli;

public static class Program
{
    private const string Logo = "🐈";
    private const string Version = "1.0.0";

    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand($"{Logo} nbot v{Version} - A lightweight personal AI assistant");

        var versionOption = new Option<bool>(
            name: "--version",
            description: "Show version information"
        );
        versionOption.AddAlias("-v");

        rootCommand.AddGlobalOption(versionOption);

        var commands = GetCommands();
        foreach (var cmd in commands)
        {
            rootCommand.AddCommand(CreateCommand(cmd));
        }

        rootCommand.SetHandler((version) =>
        {
            if (version)
            {
                Console.WriteLine($"{Logo} nbot v{Version}");
            }
            else
            {
                rootCommand.Invoke("--help");
            }
        }, versionOption);

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

    private static Command CreateCommand(ICliCommand cliCommand)
    {
        var command = new Command(cliCommand.Name, cliCommand.Description);

        command.SetHandler(async (context) =>
        {
            var remainingArgs = context.ParseResult.Tokens
                .Where(t => t.Type != System.CommandLine.Parsing.TokenType.Command)
                .Select(t => t.Value)
                .ToArray();

            var exitCode = await cliCommand.ExecuteAsync(remainingArgs, context.GetCancellationToken());
            context.ExitCode = exitCode;
        });

        return command;
    }
}
