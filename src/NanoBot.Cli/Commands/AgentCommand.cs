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

    private static readonly HashSet<string> SessionCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "/new", "/list", "/resume", "/clear", "/sessions"
    };

    public Command CreateCommand()
    {
        var messageOption = new Option<string?>(
            name: "--message",
            description: "Message to send to the agent"
        );
        messageOption.AddAlias("-m");

        var sessionOption = new Option<string?>(
            name: "--session",
            description: "Session ID (omit to use the last used session)"
        );
        sessionOption.AddAlias("-s");

        var listSessionsOption = new Option<bool>(
            name: "--list-sessions",
            description: "List all sessions and exit",
            getDefaultValue: () => false
        );

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
            streamingOption,
            listSessionsOption
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
            var listSessions = context.ParseResult.GetValueForOption(listSessionsOption);
            var cancellationToken = context.GetCancellationToken();
            await ExecuteAgentAsync(message, session, configPath, markdown, logs, skipCheck, streaming, listSessions, cancellationToken);
        });

        return command;
    }

    private async Task ExecuteAgentAsync(
        string? message,
        string? sessionId,
        string? configPath,
        bool renderMarkdown,
        bool showLogs,
        bool skipCheck,
        bool streaming,
        bool listSessions,
        CancellationToken cancellationToken)
    {
        var resolvedConfigPath = ConfigurationChecker.ResolveExistingConfigPath(configPath) ?? configPath;

        if (!skipCheck)
        {
            var checkResult = await ConfigurationChecker.CheckAsync(resolvedConfigPath, cancellationToken);
            if (!checkResult.IsReady)
            {
                PrintConfigurationGuidance(checkResult);
                return;
            }
        }

        var config = await LoadConfigAsync(resolvedConfigPath, cancellationToken);

        var services = new ServiceCollection();

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

        services.AddNanoBot(config);

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
        var sessionManager = serviceProvider.GetRequiredService<ISessionManager>();

        // Get or create the current session
        var currentSessionId = sessionId ?? GetLastSessionId(workspace) ?? $"chat_{Guid.NewGuid():N}";

        // If --list-sessions is specified, show all sessions and exit
        if (listSessions)
        {
            await ListAllSessionsAsync(sessionManager, workspace);
            return;
        }

        try
        {
            if (!string.IsNullOrEmpty(message))
            {
                await RunSingleMessageAsync(runtime, message, currentSessionId, renderMarkdown, streaming, cancellationToken);
            }
            else
            {
                await RunInteractiveAsync(runtime, sessionManager, workspace, currentSessionId, renderMarkdown, streaming, cancellationToken);
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

    private static string? GetLastSessionId(IWorkspaceManager workspace)
    {
        try
        {
            var sessionsPath = workspace.GetSessionsPath();
            var lastSessionFile = Path.Combine(sessionsPath, ".last_session");
            if (File.Exists(lastSessionFile))
            {
                var sessionId = File.ReadAllText(lastSessionFile).Trim();
                if (!string.IsNullOrEmpty(sessionId) && sessionId.StartsWith("chat_"))
                {
                    return sessionId;
                }
            }
        }
        catch
        {
            // Ignore errors
        }
        return null;
    }

    private static async Task SaveLastSessionIdAsync(IWorkspaceManager workspace, string sessionId)
    {
        try
        {
            var sessionsPath = workspace.GetSessionsPath();
            Directory.CreateDirectory(sessionsPath);
            var lastSessionFile = Path.Combine(sessionsPath, ".last_session");
            await File.WriteAllTextAsync(lastSessionFile, sessionId);
        }
        catch
        {
            // Ignore errors
        }
    }

    private static async Task ListAllSessionsAsync(ISessionManager sessionManager, IWorkspaceManager workspace)
    {
        var sessions = sessionManager.ListSessions()
            .Where(s => s.Key.StartsWith("chat_"))
            .OrderByDescending(s => s.UpdatedAt ?? s.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();

        Console.WriteLine("\n=== CLI Sessions ===\n");

        if (sessions.Count == 0)
        {
            Console.WriteLine("No CLI sessions found.");
            Console.WriteLine("Use /new to create a new session.\n");
            return;
        }

        foreach (var session in sessions)
        {
            var title = session.Title ?? "Untitled";
            var created = session.CreatedAt?.ToString("yyyy-MM-dd HH:mm") ?? "Unknown";
            var updated = session.UpdatedAt?.ToString("yyyy-MM-dd HH:mm") ?? "Unknown";
            var key = session.Key.Replace("chat_", "");

            Console.WriteLine($"  {key}");
            Console.WriteLine($"    Title: {title}");
            Console.WriteLine($"    Created: {created}");
            Console.WriteLine($"    Updated: {updated}");
            Console.WriteLine();
        }

        Console.WriteLine("Use --session <id> to resume a specific session.\n");
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
        Console.WriteLine("Run onboarding to create or update your configuration:");
        Console.WriteLine();
        Console.WriteLine("  nbot onboard");
        Console.WriteLine();
        Console.WriteLine("Or with options (non-interactive):");
        Console.WriteLine("  nbot onboard --non-interactive --provider openai --model gpt-4o-mini --api-key YOUR_KEY");
        Console.WriteLine();
        Console.WriteLine("  nbot onboard --non-interactive --provider ollama --model qwen3.5:4b --api-base http://localhost:11434/v1");
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
            Console.WriteLine("🐈 NBot is thinking...\n");
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
        Console.Write("🐈 NBot：");
        Console.Out.Flush();

        var fullResponse = new StringBuilder();
        var isFirstChunk = true;
        var hasOutputContent = false;

        await foreach (var update in runtime.ProcessDirectStreamingAsync(message, sessionId, cancellationToken: cancellationToken))
        {
            var text = update.Text;

            // Check if this is a tool hint
            var isToolHint = update.AdditionalProperties?.ContainsKey("_tool_hint") == true;
            var isToolResult = update.AdditionalProperties?.ContainsKey("_tool_result") == true;

            if (isToolHint)
            {
                // Tool hint: output on its own line with color/formatting
                if (hasOutputContent)
                {
                    Console.WriteLine();
                    hasOutputContent = false;
                }
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("⚡ ");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(text.Trim());
                Console.ResetColor();
                Console.WriteLine();
                isFirstChunk = true; // Reset for next content
                continue;
            }

            if (isToolResult)
            {
                // Tool result: show in a distinct format
                if (hasOutputContent)
                {
                    Console.WriteLine();
                    hasOutputContent = false;
                }
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("✓ ");
                Console.ResetColor();
                Console.Write(text?.Trim());
                Console.WriteLine();
                isFirstChunk = true; // Reset for next content
                continue;
            }

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
            hasOutputContent = true;
        }

        Console.WriteLine();
        Console.WriteLine();
    }

    private static async Task RunInteractiveAsync(
        IAgentRuntime runtime,
        ISessionManager sessionManager,
        IWorkspaceManager workspace,
        string sessionId,
        bool renderMarkdown,
        bool streaming,
        CancellationToken cancellationToken)
    {
        var currentSessionId = sessionId;

        // Save the session ID as the last used session
        await SaveLastSessionIdAsync(workspace, currentSessionId);

        Console.WriteLine("🐈 NBot Interactive mode (type 'exit' or Ctrl+C to quit)");
        Console.WriteLine($"📋 Current session: {currentSessionId.Replace("chat_", "")}");
        Console.WriteLine("💡 Use /new, /list, /resume, /clear to manage sessions\n");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var cancelPressedOnce = false;

        Console.CancelKeyPress += (sender, e) =>
        {
            if (!cancelPressedOnce)
            {
                e.Cancel = true;
                cancelPressedOnce = true;
                Console.WriteLine("\n再按一次 Ctrl+C 退出应用");
                Console.Write("You: ");
                Console.Out.Flush();
                Task.Delay(2000).ContinueWith(_ => cancelPressedOnce = false);
            }
            else
            {
                e.Cancel = false;
                Console.WriteLine("\nGoodbye!");
            }
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

            var trimmedInput = input.Trim();

            if (ExitCommands.Contains(trimmedInput))
            {
                Console.WriteLine("\nGoodbye!");
                break;
            }

            // Handle session management commands
            if (trimmedInput.StartsWith("/new") || trimmedInput.Equals("/n", StringComparison.OrdinalIgnoreCase))
            {
                currentSessionId = $"chat_{Guid.NewGuid():N}";
                await SaveLastSessionIdAsync(workspace, currentSessionId);
                Console.WriteLine($"\n✓ Created new session: {currentSessionId.Replace("chat_", "")}");
                Console.WriteLine("📋 Current session: {0}\n", currentSessionId.Replace("chat_", ""));
                continue;
            }

            if (trimmedInput.StartsWith("/list") || trimmedInput.Equals("/l", StringComparison.OrdinalIgnoreCase))
            {
                await ListAllSessionsAsync(sessionManager, workspace);
                Console.WriteLine($"📋 Current session: {currentSessionId.Replace("chat_", "")}\n");
                continue;
            }

            if (trimmedInput.StartsWith("/resume") || trimmedInput.StartsWith("/r "))
            {
                var parts = trimmedInput.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    var targetSession = parts[1].Trim();
                    if (!targetSession.StartsWith("chat_"))
                    {
                        targetSession = $"chat_{targetSession}";
                    }

                    // Verify session exists
                    var existingSession = sessionManager.ListSessions().FirstOrDefault(s => s.Key == targetSession);
                    if (existingSession != null)
                    {
                        currentSessionId = targetSession;
                        await SaveLastSessionIdAsync(workspace, currentSessionId);
                        Console.WriteLine($"\n✓ Switched to session: {currentSessionId.Replace("chat_", "")}");
                        Console.WriteLine($"   Title: {existingSession.Title ?? "Untitled"}\n");
                    }
                    else
                    {
                        // Create new session with the given ID
                        currentSessionId = targetSession;
                        await SaveLastSessionIdAsync(workspace, currentSessionId);
                        Console.WriteLine($"\n✓ Created/resumed session: {currentSessionId.Replace("chat_", "")}\n");
                    }
                }
                else
                {
                    Console.WriteLine("\nUsage: /resume <session-id> or /r <session-id>\n");
                }
                continue;
            }

            if (trimmedInput.StartsWith("/clear") || trimmedInput.Equals("/c", StringComparison.OrdinalIgnoreCase))
            {
                await sessionManager.ClearSessionAsync(currentSessionId);
                Console.WriteLine($"\n✓ Cleared session: {currentSessionId.Replace("chat_", "")}\n");
                continue;
            }

            if (trimmedInput.Equals("/sessions") || trimmedInput.Equals("/s", StringComparison.OrdinalIgnoreCase))
            {
                await ListAllSessionsAsync(sessionManager, workspace);
                Console.WriteLine($"📋 Current session: {currentSessionId.Replace("chat_", "")}\n");
                continue;
            }

            // Check if user wants to switch session (prompt for session ID)
            if (trimmedInput.StartsWith("/switch") || trimmedInput.StartsWith("/switch "))
            {
                Console.WriteLine("\nAvailable sessions:");
                var sessions = sessionManager.ListSessions()
                    .Where(s => s.Key.StartsWith("chat_"))
                    .OrderByDescending(s => s.UpdatedAt ?? s.CreatedAt ?? DateTimeOffset.MinValue)
                    .Take(10)
                    .ToList();

                if (sessions.Count == 0)
                {
                    Console.WriteLine("  No sessions found.");
                }
                else
                {
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        var s = sessions[i];
                        var marker = s.Key == currentSessionId ? "→ " : "  ";
                        Console.WriteLine($"{marker}{i + 1}. {s.Key.Replace("chat_", "")} - {s.Title ?? "Untitled"}");
                    }
                }

                Console.Write("\nEnter session number or ID (or press Enter to cancel): ");
                var selection = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(selection))
                {
                    string? targetSessionId = null;

                    if (int.TryParse(selection.Trim(), out var index) && index > 0 && index <= sessions.Count)
                    {
                        targetSessionId = sessions[index - 1].Key;
                    }
                    else
                    {
                        var inputId = selection.Trim();
                        if (!inputId.StartsWith("chat_"))
                        {
                            inputId = $"chat_{inputId}";
                        }
                        targetSessionId = inputId;
                    }

                    if (targetSessionId != null)
                    {
                        currentSessionId = targetSessionId;
                        await SaveLastSessionIdAsync(workspace, currentSessionId);
                        Console.WriteLine($"\n✓ Switched to session: {currentSessionId.Replace("chat_", "")}\n");
                    }
                }
                else
                {
                    Console.WriteLine();
                }
                continue;
            }

            try
            {
                Console.WriteLine();
                if (streaming)
                {
                    await RunStreamingAsync(runtime, input, currentSessionId, renderMarkdown, cts.Token);
                }
                else
                {
                    Console.WriteLine("🐈 NBot is thinking...\n");
                    Console.Out.Flush();
                    var response = await runtime.ProcessDirectAsync(input, currentSessionId, cancellationToken: cts.Token);
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
        if (renderMarkdown)
        {
            Console.WriteLine("🐈 NBot：");
        }
        else
        {
            Console.WriteLine($"🐈 NBot： {response}");
            Console.WriteLine();
            return;
        }

        PrintMarkdown(response);

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
