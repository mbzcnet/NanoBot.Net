using System.CommandLine;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NanoBot.Agent;
using NanoBot.Cli.Extensions;
using NanoBot.Core.Configuration;
using NanoBot.Core.Debug;
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
        "/exit", "/quit", "/bye", "/q"
    };

    private static readonly HashSet<string> HelpCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "/help", "/?", "/h"
    };

    private static readonly HashSet<string> SessionCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "/new", "/list", "/resume", "/clear", "/sessions", "/model"
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

        var debugOption = new Option<bool>(
            name: "--debug",
            description: "Enable debug mode to log LLM requests/responses",
            getDefaultValue: () => false
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
            debugOption,
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
            var debug = context.ParseResult.GetValueForOption(debugOption);
            var listSessions = context.ParseResult.GetValueForOption(listSessionsOption);
            var cancellationToken = context.GetCancellationToken();
            await ExecuteAgentAsync(message, session, configPath, markdown, logs, skipCheck, streaming, debug, listSessions, cancellationToken);
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
        bool debug,
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

        // Enable debug mode if --debug flag is provided
        if (debug)
        {
            var debugState = serviceProvider.GetService<IDebugState>();
            if (debugState != null)
            {
                debugState.EnableDebug(currentSessionId);
            }
        }

        // If --list-sessions is specified, show all sessions and exit
        if (listSessions)
        {
            await ListAllSessionsAsync(sessionManager, workspace);
            return;
        }

        // Main loop - allows restarting after model switch
        var restartNeeded = false;
        do
        {
            try
            {
                // Re-create services and runtime with current config
                var servicesLoop = new ServiceCollection();

                // Configure NLog - load from executable directory for correct path when running from any cwd
                var nlogPathLoop = Path.Combine(AppContext.BaseDirectory, "nlog.config");
                if (!File.Exists(nlogPathLoop))
                {
                    nlogPathLoop = "nlog.config";
                }
                NLog.LogManager.Configuration = new XmlLoggingConfiguration(nlogPathLoop);
                servicesLoop.AddLogging(builder =>
                {
                    builder.ClearProviders();
                    builder.AddNLog();
                });

                servicesLoop.AddNanoBot(config);

                if (!showLogs)
                {
                    servicesLoop.Configure<LoggerFilterOptions>(options =>
                    {
                        options.MinLevel = Microsoft.Extensions.Logging.LogLevel.Warning;
                    });
                }

                var serviceProviderLoop = servicesLoop.BuildServiceProvider();

                var workspaceForRuntime = serviceProviderLoop.GetRequiredService<IWorkspaceManager>();
                await workspaceForRuntime.InitializeAsync(cancellationToken);

                var runtimeForThisLoop = serviceProviderLoop.GetRequiredService<IAgentRuntime>();

                if (debug)
                {
                    var debugState = serviceProviderLoop.GetService<IDebugState>();
                    if (debugState != null)
                    {
                        debugState.EnableDebug(currentSessionId);
                    }
                }

                try
                {
                    if (!string.IsNullOrEmpty(message))
                    {
                        await RunSingleMessageAsync(runtimeForThisLoop, message, currentSessionId, renderMarkdown, streaming, cancellationToken);
                    }
                    else
                    {
                        var shouldRestart = await RunInteractiveAsync(runtimeForThisLoop, sessionManager, workspace, currentSessionId, renderMarkdown, streaming, cancellationToken, config, resolvedConfigPath);
                        if (shouldRestart)
                        {
                            restartNeeded = true;
                            // Reload config from disk
                            config = await LoadConfigAsync(resolvedConfigPath, cancellationToken);
                        }
                    }
                }
                finally
                {
                    if (runtimeForThisLoop is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                restartNeeded = false; // Don't restart on error
            }
        } while (restartNeeded);
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

    private static async Task<bool> RunInteractiveAsync(
        IAgentRuntime runtime,
        ISessionManager sessionManager,
        IWorkspaceManager workspace,
        string sessionId,
        bool renderMarkdown,
        bool streaming,
        CancellationToken cancellationToken,
        AgentConfig config,
        string? resolvedConfigPath)
    {
        var currentSessionId = sessionId;

        // Save the session ID as the last used session
        await SaveLastSessionIdAsync(workspace, currentSessionId);

        Console.WriteLine("🐈 NBot Interactive mode (type '/help' for commands, '/exit' to quit)");
        Console.WriteLine($"📋 Current session: {currentSessionId.Replace("chat_", "")}");
        Console.WriteLine("💡 Type '/help' to see all available commands\n");

        var restartNeeded = false;

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

            // Handle help command
            if (HelpCommands.Contains(trimmedInput))
            {
                PrintHelp();
                Console.WriteLine($"📋 Current session: {currentSessionId.Replace("chat_", "")}\n");
                continue;
            }

            if (ExitCommands.Contains(trimmedInput))
            {
                Console.WriteLine("\nGoodbye! 👋");
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

            // Handle model switch command
            if (trimmedInput.StartsWith("/model"))
            {
                var parts = trimmedInput.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 1)
                {
                    // Show current model
                    PrintCurrentModel(config);
                }
                else
                {
                    // Switch to specified model or profile
                    var targetName = parts[1].Trim();
                    var switched = await SwitchModelAsync(config, targetName, resolvedConfigPath, cancellationToken);
                    if (switched)
                    {
                        // Signal restart
                        restartNeeded = true;
                        Console.WriteLine("\n✓ Model switched successfully. Restarting with new model...\n");
                        return true; // Return true to signal restart
                    }
                }
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

        return restartNeeded;
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

    private static void PrintCurrentModel(AgentConfig config)
    {
        Console.WriteLine("\n=== Current Model ===\n");

        var profileName = string.IsNullOrEmpty(config.Llm.DefaultProfile) ? "default" : config.Llm.DefaultProfile;
        var profile = config.Llm.Profiles.GetValueOrDefault(profileName);

        if (profile == null)
        {
            Console.WriteLine("No LLM profile configured.");
            Console.WriteLine("Run 'nbot onboard' to configure your model.\n");
            return;
        }

        Console.WriteLine($"Profile: {profileName}");
        Console.WriteLine($"Provider: {profile.Provider ?? "(not set)"}");
        Console.WriteLine($"Model: {profile.Model ?? "(not set)"}");
        Console.WriteLine($"API Base: {profile.ApiBase ?? "(default)"}");
        Console.WriteLine();

        // List all available profiles with numbers
        if (config.Llm.Profiles.Count > 0)
        {
            Console.WriteLine("Available Profiles:");
            var profilesList = config.Llm.Profiles.ToList();
            for (var i = 0; i < profilesList.Count; i++)
            {
                var kvp = profilesList[i];
                var marker = kvp.Key == profileName ? "*" : " ";
                var displayName = kvp.Value.GetDisplayName();
                Console.WriteLine($"  {marker} [{i + 1}] {kvp.Key} - {displayName}");
            }
            Console.WriteLine();
        }

        Console.WriteLine("Use '/model <number>' or '/model <profile-name>' to switch.");
        Console.WriteLine("Use '/model <provider>/<model>' to switch to a specific model (e.g., '/model openai/gpt-4o').\n");
    }

    private static async Task<bool> SwitchModelAsync(AgentConfig config, string targetName, string? configPath, CancellationToken cancellationToken)
    {
        // Check if it's a number (profile index)
        if (int.TryParse(targetName, out var index) && index > 0 && index <= config.Llm.Profiles.Count)
        {
            var profilesList = config.Llm.Profiles.ToList();
            var selectedProfile = profilesList[index - 1];

            config.Llm.DefaultProfile = selectedProfile.Key;
            Console.WriteLine($"\n✓ Switched to profile: {selectedProfile.Key}");
            Console.WriteLine($"  Provider: {selectedProfile.Value.Provider}");
            Console.WriteLine($"  Model: {selectedProfile.Value.Model}");

            // Save config if path is provided
            if (!string.IsNullOrEmpty(configPath))
            {
                try
                {
                    await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
                    Console.WriteLine($"  Configuration saved to {configPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Warning: Could not save configuration: {ex.Message}");
                }
            }

            return true;
        }

        // Check if it's a profile name
        if (config.Llm.Profiles.ContainsKey(targetName))
        {
            config.Llm.DefaultProfile = targetName;
            Console.WriteLine($"\n✓ Switched to profile: {targetName}");
            Console.WriteLine($"  Provider: {config.Llm.Profiles[targetName].Provider}");
            Console.WriteLine($"  Model: {config.Llm.Profiles[targetName].Model}");

            // Save config if path is provided
            if (!string.IsNullOrEmpty(configPath))
            {
                try
                {
                    await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
                    Console.WriteLine($"  Configuration saved to {configPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Warning: Could not save configuration: {ex.Message}");
                }
            }

            return true;
        }

        // Check if it's in format "provider/model"
        if (targetName.Contains('/'))
        {
            var parts = targetName.Split('/', 2);
            var provider = parts[0].ToLowerInvariant();
            var model = parts[1];

            // Validate provider
            if (!ConfigurationChecker.SupportedProviders.Contains(provider))
            {
                Console.WriteLine($"\nUnknown provider: {provider}");
                Console.WriteLine($"Supported providers: {string.Join(", ", ConfigurationChecker.SupportedProviders)}\n");
                return false;
            }

            // Create or update profile
            var profileName = string.IsNullOrEmpty(config.Llm.DefaultProfile) ? "default" : config.Llm.DefaultProfile;
            if (!config.Llm.Profiles.ContainsKey(profileName))
            {
                config.Llm.Profiles[profileName] = new LlmProfile { Name = profileName };
            }

            var profile = config.Llm.Profiles[profileName];
            profile.Provider = provider;
            profile.Model = model;

            // Set default API base if not set
            if (string.IsNullOrEmpty(profile.ApiBase) && ConfigurationChecker.ProviderApiBases.TryGetValue(provider, out var apiBase))
            {
                profile.ApiBase = apiBase;
            }

            Console.WriteLine($"\n✓ Updated profile '{profileName}':");
            Console.WriteLine($"  Provider: {profile.Provider}");
            Console.WriteLine($"  Model: {profile.Model}");
            Console.WriteLine($"  API Base: {profile.ApiBase ?? "(default)"}");

            // Save config if path is provided
            if (!string.IsNullOrEmpty(configPath))
            {
                try
                {
                    await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
                    Console.WriteLine($"  Configuration saved to {configPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Warning: Could not save configuration: {ex.Message}");
                }
            }

            Console.WriteLine("\n⚠️  API key may need to be set via environment variable or config file.");
            return true;
        }

        // Try to find a profile by model name (partial match)
        var matchingProfile = config.Llm.Profiles.FirstOrDefault(
            kvp => kvp.Value.Model?.Contains(targetName, StringComparison.OrdinalIgnoreCase) == true);

        if (matchingProfile.Key != null)
        {
            config.Llm.DefaultProfile = matchingProfile.Key;
            Console.WriteLine($"\n✓ Switched to profile: {matchingProfile.Key}");
            Console.WriteLine($"  Provider: {matchingProfile.Value.Provider}");
            Console.WriteLine($"  Model: {matchingProfile.Value.Model}");

            // Save config if path is provided
            if (!string.IsNullOrEmpty(configPath))
            {
                try
                {
                    await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
                    Console.WriteLine($"  Configuration saved to {configPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Warning: Could not save configuration: {ex.Message}");
                }
            }

            return true;
        }

        Console.WriteLine($"\nUnknown model or profile: {targetName}");
        Console.WriteLine("\nTo switch to a profile, use: /model <profile-name>");
        Console.WriteLine("To switch to a specific model, use: /model <provider>/<model>");
        Console.WriteLine($"\nSupported providers: {string.Join(", ", ConfigurationChecker.SupportedProviders)}\n");

        return false;
    }

    private static void PrintHelp()
    {
        Console.WriteLine(@"
╔══════════════════════════════════════════════════════════╗
║                    🐈 NBot Commands                      ║
╠══════════════════════════════════════════════════════════╣
║  General                                                 ║
║    /help, /?     Show this help message                  ║
║    /exit, /quit  Exit the application                    ║
║    /bye, /q      Exit the application (shortcut)         ║
╠══════════════════════════════════════════════════════════╣
║  Session Management                                      ║
║    /new, /n      Create a new session                    ║
║    /list, /l     List all sessions                       ║
║    /sessions, /s Show session list                       ║
║    /resume <id>  Resume a session (e.g., /resume abc123) ║
║    /switch       Interactively switch sessions           ║
║    /clear, /c    Clear current session history           ║
╠══════════════════════════════════════════════════════════╣
║  Model Configuration                                     ║
║    /model        Show current model configuration        ║
║    /model <N>    Switch by profile number (e.g., /model 1)║
║    /model <name> Switch by profile name                  ║
║    /model <p/m>  Switch by provider/model (e.g., openai/ │
║                  gpt-4o)                                 ║
╠══════════════════════════════════════════════════════════╣
║  Tips                                                    ║
║    - Press Ctrl+C once to cancel current operation       ║
║    - Press Ctrl+C twice to exit                          ║
║    - Session IDs are saved for quick resume              ║
║    - Model switches take effect immediately              ║
╚══════════════════════════════════════════════════════════╝
");
    }
}
