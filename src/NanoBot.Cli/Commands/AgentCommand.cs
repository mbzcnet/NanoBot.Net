using System.CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NanoBot.Agent;
using NanoBot.Cli.Extensions;
using NanoBot.Core.Configuration;
using NanoBot.Core.Workspace;

namespace NanoBot.Cli.Commands;

public class AgentCommand : ICliCommand
{
    public string Name => "agent";
    public string Description => "Start Agent interactive mode";

    private static readonly HashSet<string> ExitCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "exit", "quit", "/exit", "/quit", ":q"
    };

    public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var messageOption = new Option<string?>(
            name: "--message",
            description: "Message to send to the agent"
        );
        messageOption.AddAlias("-m");

        var sessionOption = new Option<string>(
            name: "--session",
            description: "Session ID",
            getDefaultValue: () => "cli:direct"
        );
        sessionOption.AddAlias("-s");

        var configOption = new Option<string?>(
            name: "--config",
            description: "Configuration file path"
        );
        configOption.AddAlias("-c");

        var markdownOption = new Option<bool>(
            name: "--markdown",
            description: "Render output as Markdown",
            getDefaultValue: () => true
        );

        var logsOption = new Option<bool>(
            name: "--logs",
            description: "Show runtime logs during chat",
            getDefaultValue: () => false
        );

        var command = new Command(Name, Description)
        {
            messageOption,
            sessionOption,
            configOption,
            markdownOption,
            logsOption
        };

        command.SetHandler(async (message, session, configPath, markdown, logs) =>
        {
            await ExecuteAgentAsync(message, session, configPath, markdown, logs, cancellationToken);
        }, messageOption, sessionOption, configOption, markdownOption, logsOption);

        return await command.InvokeAsync(args);
    }

    private async Task ExecuteAgentAsync(
        string? message,
        string sessionId,
        string? configPath,
        bool renderMarkdown,
        bool showLogs,
        CancellationToken cancellationToken)
    {
        var config = await LoadConfigAsync(configPath, cancellationToken);

        var services = new ServiceCollection();
        var configuration = BuildConfiguration(configPath);

        services.AddNanoBot(configuration);

        if (!showLogs)
        {
            services.Configure<LoggerFilterOptions>(options =>
            {
                options.MinLevel = LogLevel.Warning;
            });
        }

        var serviceProvider = services.BuildServiceProvider();

        var workspace = serviceProvider.GetRequiredService<IWorkspaceManager>();
        await workspace.InitializeAsync(cancellationToken);

        var runtime = serviceProvider.GetRequiredService<IAgentRuntime>();

        try
        {
            if (!string.IsNullOrEmpty(message))
            {
                await RunSingleMessageAsync(runtime, message, sessionId, renderMarkdown, cancellationToken);
            }
            else
            {
                await RunInteractiveAsync(runtime, sessionId, renderMarkdown, cancellationToken);
            }
        }
        finally
        {
            if (runtime is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private static async Task<AgentConfig> LoadConfigAsync(string? configPath, CancellationToken cancellationToken)
    {
        return await ConfigurationLoader.LoadWithDefaultsAsync(configPath, cancellationToken);
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

    private static async Task RunSingleMessageAsync(
        IAgentRuntime runtime,
        string message,
        string sessionId,
        bool renderMarkdown,
        CancellationToken cancellationToken)
    {
        Console.WriteLine("🐈 nbot is thinking...\n");

        var response = await runtime.ProcessDirectAsync(message, sessionId, cancellationToken: cancellationToken);
        PrintAgentResponse(response, renderMarkdown);
    }

    private static async Task RunInteractiveAsync(
        IAgentRuntime runtime,
        string sessionId,
        bool renderMarkdown,
        CancellationToken cancellationToken)
    {
        Console.WriteLine("🐈 nbot Interactive mode (type 'exit' or Ctrl+C to quit)\n");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            Console.WriteLine("\nGoodbye!");
        };

        while (!cts.Token.IsCancellationRequested)
        {
            Console.Write("You: ");
            var input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
            {
                continue;
            }

            if (ExitCommands.Contains(input.Trim()))
            {
                Console.WriteLine("\nGoodbye!");
                break;
            }

            try
            {
                Console.WriteLine();
                Console.WriteLine("🐈 nbot is thinking...\n");

                var response = await runtime.ProcessDirectAsync(input, sessionId, cancellationToken: cts.Token);
                PrintAgentResponse(response, renderMarkdown);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }

    private static void PrintAgentResponse(string response, bool renderMarkdown)
    {
        Console.WriteLine();
        Console.WriteLine("🐈 nbot");

        if (renderMarkdown)
        {
            PrintMarkdown(response);
        }
        else
        {
            Console.WriteLine(response);
        }

        Console.WriteLine();
    }

    private static void PrintMarkdown(string content)
    {
        var lines = content.Split('\n');
        foreach (var line in lines)
        {
            var processedLine = ProcessMarkdownLine(line);
            Console.WriteLine(processedLine);
        }
    }

    private static string ProcessMarkdownLine(string line)
    {
        if (line.StartsWith("### "))
        {
            return $"\n{line[4..]}\n";
        }
        if (line.StartsWith("## "))
        {
            return $"\n{line[3..]}\n";
        }
        if (line.StartsWith("# "))
        {
            return $"\n{line[2..]}\n";
        }
        if (line.StartsWith("- ") || line.StartsWith("* "))
        {
            return $"  • {line[2..]}";
        }
        if (line.StartsWith("```"))
        {
            return line;
        }
        return line;
    }
}
