using System.CommandLine;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NanoBot.Agent;
using NanoBot.Cli.Extensions;
using NanoBot.Core.Bus;
using NanoBot.Core.Channels;
using NanoBot.Core.Configuration;
using NanoBot.Core.Cron;
using NanoBot.Core.Debug;
using NanoBot.Core.Heartbeat;
using NanoBot.Core.Memory;
using NanoBot.Core.Skills;
using NanoBot.Core.Storage;
using NanoBot.Core.Subagents;
using NanoBot.Core.Tools.Browser;
using NanoBot.Core.Tools.Rpa;
using NanoBot.Core.Workspace;
using NanoBot.Infrastructure.Resources;
using NanoBot.Providers;
using NLog.Config;
using NLog.Extensions.Logging;
using Spectre.Console;

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
            description: "Enable debug mode (log LLM requests/responses and runtime logs)",
            getDefaultValue: () => false
        );

        var command = new Command(Name, Description)
        {
            messageOption,
            sessionOption,
            configOption,
            markdownOption,
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
            var skipCheck = context.ParseResult.GetValueForOption(skipCheckOption);
            var streaming = context.ParseResult.GetValueForOption(streamingOption);
            var debug = context.ParseResult.GetValueForOption(debugOption);
            var listSessions = context.ParseResult.GetValueForOption(listSessionsOption);
            var cancellationToken = context.GetCancellationToken();
            await ExecuteAgentAsync(message, session, configPath, markdown, skipCheck, streaming, debug, listSessions, cancellationToken);
        });

        return command;
    }

    private static void ConfigureNLog()
    {
        var nlogPath = Path.Combine(AppContext.BaseDirectory, "nlog.config");
        if (!File.Exists(nlogPath))
            nlogPath = "nlog.config";
        NLog.LogManager.Configuration = new XmlLoggingConfiguration(nlogPath);
    }

    private static IServiceCollection BuildLoggingServices(bool debug)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddNLog();
            if (!debug)
                builder.SetMinimumLevel(LogLevel.Warning);
        });
        return services;
    }

    private async Task ExecuteAgentAsync(
        string? message,
        string? sessionId,
        string? configPath,
        bool renderMarkdown,
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
        LogResolvedConfiguration("CLI", resolvedConfigPath, config, string.IsNullOrEmpty(resolvedConfigPath) || !File.Exists(resolvedConfigPath));

        // Build base service provider once (shared infrastructure: bus, workspace, cron, etc.)
        ConfigureNLog();
        var baseServices = BuildLoggingServices(debug);
        NanoBot.Agent.ServiceCollectionExtensions.AddNanoBot(baseServices, config);
        var baseServiceProvider = baseServices.BuildServiceProvider();

        var workspace = baseServiceProvider.GetRequiredService<IWorkspaceManager>();
        await workspace.InitializeAsync(cancellationToken);
        var sessionManager = baseServiceProvider.GetRequiredService<ISessionManager>();

        // Get or create the current session
        var currentSessionId = sessionId ?? GetLastSessionId(workspace) ?? $"chat_{Guid.NewGuid():N}";

        // Enable debug mode if --debug flag is provided
        if (debug)
        {
            var debugState = baseServiceProvider.GetService<IDebugState>();
            debugState?.EnableDebug(currentSessionId);
        }

        // If --list-sessions is specified, show all sessions and exit
        if (listSessions)
        {
            await ListAllSessionsAsync(sessionManager, workspace);
            return;
        }

        // Main loop - rebuild only ChatClient + AgentRuntime on model switch
        var restartNeeded = false;
        do
        {
            // Rebuild only model-specific services (IChatClient + AgentRuntime)
            // Infrastructure singletons are forwarded from the base provider.
            ConfigureNLog();
            var modelServices = BuildLoggingServices(debug);
            ForwardSharedSingletons(modelServices, baseServiceProvider);
            modelServices.AddSingleton(config);
            modelServices.AddSingleton(config.Workspace);
            modelServices.AddSingleton(config.Llm);
            modelServices.AddSingleton(config.Security);
            modelServices.AddSingleton(config.Memory);
            modelServices.AddMicrosoftAgentsAI(config.Llm);
            NanoBot.Agent.ServiceCollectionExtensions.AddNanoBotAgent(modelServices);
            var modelServiceProvider = modelServices.BuildServiceProvider();

            var runtimeForThisLoop = modelServiceProvider.GetRequiredService<IAgentRuntime>();

            if (debug)
            {
                var debugState = modelServiceProvider.GetService<IDebugState>();
                debugState?.EnableDebug(currentSessionId);
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
                        LogResolvedConfiguration("CLI-Reload", resolvedConfigPath, config, string.IsNullOrEmpty(resolvedConfigPath) || !File.Exists(resolvedConfigPath));
                    }
                }
            }
            finally
            {
                if (runtimeForThisLoop is IDisposable disposable)
                    disposable.Dispose();
            }
        } while (restartNeeded);
    }

    private static void ForwardSharedSingletons(IServiceCollection services, IServiceProvider baseServiceProvider)
    {
        services.AddSingleton(baseServiceProvider.GetRequiredService<IWorkspaceManager>());
        services.AddSingleton(baseServiceProvider.GetRequiredService<IMessageBus>());
        services.AddSingleton(baseServiceProvider.GetRequiredService<ISkillsLoader>());

        var memoryStore = baseServiceProvider.GetService<IMemoryStore>();
        if (memoryStore != null)
            services.AddSingleton(memoryStore);

        var subagentManager = baseServiceProvider.GetService<ISubagentManager>();
        if (subagentManager != null)
            services.AddSingleton(subagentManager);

        var debugState = baseServiceProvider.GetService<IDebugState>();
        if (debugState != null)
            services.AddSingleton(debugState);

        foreach (var tool in baseServiceProvider.GetServices<AITool>())
            services.AddSingleton(typeof(AITool), tool);
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

    private static void LogResolvedConfiguration(string source, string? configPath, AgentConfig config, bool usingDefaultConfig)
    {
        var defaultProfileId = config.Llm.DefaultProfile ?? "default";
        config.Llm.Profiles.TryGetValue(defaultProfileId, out var profile);

        Console.WriteLine($"[NanoBot Config] source={source} configPath={(string.IsNullOrWhiteSpace(configPath) ? "<default>" : configPath)} usingDefaultConfig={usingDefaultConfig}");
        Console.WriteLine($"[NanoBot Config] workspace={config.Workspace.Path} defaultProfile={defaultProfileId} provider={profile?.Provider ?? "openai"} model={profile?.Model ?? "<unknown>"} apiBase={profile?.ApiBase ?? "<null>"} maxTokens={profile?.MaxTokens}");
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
            RenderMarkdown(response);
        }
        else
        {
            Console.WriteLine($"🐈 NBot： {response}");
            Console.WriteLine();
            return;
        }

        Console.WriteLine();
    }

    private static void RenderMarkdown(string content)
    {
        if (string.IsNullOrEmpty(content))
            return;

        var lines = content.Split('\n');
        foreach (var line in lines)
        {
            var processedLine = ProcessMarkdownLine(line);
            try
            {
                AnsiConsole.WriteLine(processedLine);
            }
            catch
            {
                Console.WriteLine(processedLine);
            }
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
