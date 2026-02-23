using System.CommandLine;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NanoBot.Agent;
using NanoBot.Cli.Extensions;
using NanoBot.Core.Configuration;
using NanoBot.Core.Workspace;
using NLog.Config;
using NLog.Extensions.Logging;

namespace NanoBot.Cli.Commands;

public class AgentCommand : ICliCommand
{
    public string Name => "agent";
    public string Description => "Start Agent interactive mode";

    private static readonly HashSet<string> ExitCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "exit", "quit", "/exit", "/quit", ":q"
    };

    public Command CreateCommand()
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

        var skipCheckOption = new Option<bool>(
            name: "--skip-check",
            description: "Skip configuration check",
            getDefaultValue: () => false
        );

        var streamingOption = new Option<bool>(
            name: "--streaming",
            description: "Enable streaming output",
            getDefaultValue: () => true
        );

        var command = new Command(Name, Description)
        {
            messageOption,
            sessionOption,
            configOption,
            markdownOption,
            logsOption,
            skipCheckOption,
            streamingOption
        };

        command.SetHandler(async (context) =>
        {
            var message = context.ParseResult.GetValueForOption(messageOption);
            var session = context.ParseResult.GetValueForOption(sessionOption);
            var configPath = context.ParseResult.GetValueForOption(configOption);
            var markdown = context.ParseResult.GetValueForOption(markdownOption);
            var logs = context.ParseResult.GetValueForOption(logsOption);
            var skipCheck = context.ParseResult.GetValueForOption(skipCheckOption);
            var streaming = context.ParseResult.GetValueForOption(streamingOption);
            var cancellationToken = context.GetCancellationToken();
            await ExecuteAgentAsync(message, session, configPath, markdown, logs, skipCheck, streaming, cancellationToken);
        });

        return command;
    }

    private async Task ExecuteAgentAsync(
        string? message,
        string sessionId,
        string? configPath,
        bool renderMarkdown,
        bool showLogs,
        bool skipCheck,
        bool streaming,
        CancellationToken cancellationToken)
    {
        if (!skipCheck)
        {
            var checkResult = await ConfigurationChecker.CheckAsync(configPath, cancellationToken);
            if (!checkResult.IsReady)
            {
                PrintConfigurationGuidance(checkResult);
                return;
            }
        }

        var config = await LoadConfigAsync(configPath, cancellationToken);

        var services = new ServiceCollection();
        var configuration = BuildConfiguration(configPath);

        // Configure NLog - load from executable directory for correct path when running from any cwd
        var nlogPath = Path.Combine(AppContext.BaseDirectory, "nlog.config");
        if (!File.Exists(nlogPath))
        {
            nlogPath = "nlog.config";
        }
        NLog.LogManager.Configuration = new XmlLoggingConfiguration(nlogPath);
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddNLog();
        });

        services.AddNanoBot(configuration);

        if (!showLogs)
        {
            services.Configure<LoggerFilterOptions>(options =>
            {
                options.MinLevel = Microsoft.Extensions.Logging.LogLevel.Warning;
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
                await RunSingleMessageAsync(runtime, message, sessionId, renderMarkdown, streaming, cancellationToken);
            }
            else
            {
                await RunInteractiveAsync(runtime, sessionId, renderMarkdown, streaming, cancellationToken);
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

    private static void PrintConfigurationGuidance(ConfigurationCheckResult result)
    {
        Console.WriteLine("🐈 nbot - Configuration Required\n");

        if (!result.ConfigExists)
        {
            Console.WriteLine("Configuration file not found.");
        }
        else if (result.MissingFields.Count > 0)
        {
            Console.WriteLine("Configuration is incomplete:");
            foreach (var field in result.MissingFields)
            {
                Console.WriteLine($"  • Missing: {field}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Run the configuration wizard to get started:");
        Console.WriteLine();
        Console.WriteLine("  nbot configure");
        Console.WriteLine();
        Console.WriteLine("Or set up manually:");
        Console.WriteLine("  nbot configure --provider openai --model gpt-4o-mini --api-key YOUR_KEY");
        Console.WriteLine();
        Console.WriteLine("For non-interactive setup:");
        Console.WriteLine("  nbot configure --non-interactive --provider openai --api-key YOUR_KEY");
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
        bool streaming,
        CancellationToken cancellationToken)
    {
        if (streaming)
        {
            await RunStreamingAsync(runtime, message, sessionId, renderMarkdown, cancellationToken);
        }
        else
        {
            Console.WriteLine("🐈 nbot is thinking...\n");
            var response = await runtime.ProcessDirectAsync(message, sessionId, cancellationToken: cancellationToken);
            PrintAgentResponse(response, renderMarkdown);
        }
    }

    private static async Task RunStreamingAsync(
        IAgentRuntime runtime,
        string message,
        string sessionId,
        bool renderMarkdown,
        CancellationToken cancellationToken)
    {
        Console.Write("🐈 nbot");
        Console.Out.Flush();

        var fullResponse = new StringBuilder();
        var isFirstChunk = true;

        await foreach (var update in runtime.ProcessDirectStreamingAsync(message, sessionId, cancellationToken: cancellationToken))
        {
            var text = update.Text;
            if (string.IsNullOrEmpty(text))
                continue;

            if (isFirstChunk)
            {
                Console.Write(" ");
                isFirstChunk = false;
            }

            Console.Write(text);
            Console.Out.Flush();
            fullResponse.Append(text);
        }

        Console.WriteLine();
        Console.WriteLine();
    }

    private static async Task RunInteractiveAsync(
        IAgentRuntime runtime,
        string sessionId,
        bool renderMarkdown,
        bool streaming,
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
            Console.Out.Flush();
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
                if (streaming)
                {
                    await RunStreamingAsync(runtime, input, sessionId, renderMarkdown, cts.Token);
                }
                else
                {
                    Console.WriteLine("🐈 nbot is thinking...\n");
                    Console.Out.Flush();
                    var response = await runtime.ProcessDirectAsync(input, sessionId, cancellationToken: cts.Token);
                    PrintAgentResponse(response, renderMarkdown);
                }
                Console.Out.Flush();
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
