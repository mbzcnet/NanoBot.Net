using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NanoBot.Agent.Extensions;
using NanoBot.Core.Bus;
using NanoBot.Core.Configuration;
using NanoBot.Core.Debug;
using NanoBot.Core.Memory;
using NanoBot.Core.Skills;
using NanoBot.Core.Subagents;
using NanoBot.Core.Tools;
using NanoBot.Core.Workspace;
using NanoBot.Providers;
using NanoBot.Agent.Services;

namespace NanoBot.Agent;

public interface IAgentRuntime
{
    Task RunAsync(CancellationToken cancellationToken = default);
    void Stop();
    Task<string> ProcessDirectAsync(string content, string sessionKey = "chat_direct", string channel = "cli", string chatId = "direct", CancellationToken cancellationToken = default);
    IAsyncEnumerable<AgentResponseUpdate> ProcessDirectStreamingAsync(string content, string sessionKey = "chat_direct", string channel = "cli", string chatId = "direct", CancellationToken cancellationToken = default);
    Task<bool> TryCancelSessionAsync(string sessionKey);
    void SetRuntimeMetadata(string sessionKey, IReadOnlyDictionary<string, string> metadata);
    void ClearAgentCache();
}

public sealed class AgentRuntime : IAgentRuntime, IDisposable
{
    private readonly ChatClientAgent _defaultAgent;
    private readonly IMessageBus _bus;
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<AgentRuntime>? _logger;
    private CancellationTokenSource? _runningCts;
    private bool _disposed;
    private bool _stopped;

    // Profile-aware chat client support
    private readonly IChatClientFactory? _chatClientFactory;
    private readonly LlmConfig? _llmConfig;
    private readonly IServiceProvider? _serviceProvider;
    private readonly ConcurrentDictionary<string, ChatClientAgent> _profileAgents = new(StringComparer.Ordinal);

    // Service instances
    private readonly MessageProcessor _messageProcessor;
    private readonly StreamingProcessor _streamingProcessor;

    // Command registry
    private readonly Dictionary<string, CommandDefinition> _commands = new(StringComparer.OrdinalIgnoreCase);

    // Session cancellation tokens
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _sessionTokens = new(StringComparer.Ordinal);

    // Runtime metadata
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> _runtimeMetadata = new(StringComparer.Ordinal);

    private record CommandDefinition(
        string Name,
        string Description,
        bool Immediate,
        Func<InboundMessage, CancellationToken, Task<OutboundMessage?>> Handler
    );

    public AgentRuntime(
        ChatClientAgent agent,
        IMessageBus bus,
        ISessionManager sessionManager,
        IWorkspaceManager workspace,
        IMemoryStore? memoryStore,
        ISubagentManager? subagentManager,
        int memoryWindow,
        IChatClientFactory? chatClientFactory = null,
        LlmConfig? llmConfig = null,
        IServiceProvider? serviceProvider = null,
        ILogger<AgentRuntime>? logger = null,
        IDebugState? debugState = null)
    {
        _defaultAgent = agent ?? throw new ArgumentNullException(nameof(agent));
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _logger = logger;
        _chatClientFactory = chatClientFactory;
        _llmConfig = llmConfig;
        _serviceProvider = serviceProvider;

        // Ensure sessions directory exists
        var sessionsDir = workspace.GetSessionsPath();
        if (!Directory.Exists(sessionsDir))
        {
            Directory.CreateDirectory(sessionsDir);
        }

        // Helper function to get ChatClient from agent
        IChatClient? GetChatClient(string? sessionKey)
        {
            var agentForSession = GetAgentForSession(sessionKey ?? "");
            return agentForSession.GetChatClientSafe();
        }

        // Helper function to get agent for session
        ChatClientAgent GetAgent(string sessionKey) => GetAgentForSession(sessionKey);

        // Helper to set session token
        void SetSessionToken(string sessionKey)
        {
            if (_sessionTokens.TryGetValue(sessionKey, out var cts))
            {
                return;
            }
            cts = new CancellationTokenSource();
            _sessionTokens.AddOrUpdate(sessionKey, _ => cts, (_, _) => cts);
        }

        // Create image processor
        var imageProcessor = new ImageContentProcessor(workspace, null);

        // Create memory consolidation service
        var memoryService = new MemoryConsolidationService(
            memoryStore,
            workspace,
            sessionManager,
            memoryWindow,
            GetChatClient,
            null);

        // Create session title manager
        var titleManager = new SessionTitleManager(
            sessionManager,
            GetChatClient,
            null);

        // Create message processor
        _messageProcessor = new MessageProcessor(
            agent,
            sessionManager,
            memoryService,
            titleManager,
            imageProcessor,
            GetAgent,
            GetChatClient,
            GetRuntimeMetadata,
            null);

        // Create streaming processor
        _streamingProcessor = new StreamingProcessor(
            agent,
            sessionManager,
            memoryService,
            titleManager,
            imageProcessor,
            GetAgent,
            SetSessionToken,
#if DEBUG
            debugState != null ? new Debug.DebugLogger(
                debugState,
                sessionManager,
                llmConfig,
                workspace,
                null) : null,
#endif
            null);

        // Register built-in commands
        RegisterCommand(new CommandDefinition(
            Name: "/new",
            Description: "Start a new conversation",
            Immediate: false,
            Handler: async (msg, ct) =>
            {
                var existingSession = await _sessionManager.GetOrCreateSessionAsync(msg.SessionKey, ct);
                return await _messageProcessor.HandleNewSessionCommandAsync(msg, existingSession, ct);
            }
        ));

        RegisterCommand(new CommandDefinition(
            Name: "/help",
            Description: "Show available commands",
            Immediate: true,
            Handler: (msg, _) => Task.FromResult<OutboundMessage?>(new OutboundMessage
            {
                Channel = msg.Channel,
                ChatId = msg.ChatId,
                Content = BuildHelpText()
            })
        ));

        RegisterCommand(new CommandDefinition(
            Name: "/stop",
            Description: "Stop the current task",
            Immediate: true,
            Handler: async (msg, _) =>
            {
                var sessionKey = msg.SessionKey;
                await TryCancelSessionAsync(sessionKey);
                return new OutboundMessage
                {
                    Channel = msg.Channel,
                    ChatId = msg.ChatId,
                    Content = "Task cancelled. Please resend your message if you want to continue."
                };
            }
        ));
    }

    private void RegisterCommand(CommandDefinition command)
    {
        _commands[command.Name] = command;
    }

    private string BuildHelpText()
    {
        var sb = new StringBuilder("nanobot commands:\n");
        foreach (var cmd in _commands.Values.OrderBy(c => c.Name))
        {
            sb.AppendLine($"{cmd.Name} - {cmd.Description}");
        }
        return sb.ToString().TrimEnd();
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _runningCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _stopped = false;

        _logger?.LogInformation("Agent runtime started");

        try
        {
            while (!_stopped && !_runningCts.Token.IsCancellationRequested)
            {
                try
                {
                    var msg = await _bus.ConsumeInboundAsync(_runningCts.Token);

                    try
                    {
                        // Try to handle as a command first
                        var commandResult = await TryHandleCommandAsync(msg, _runningCts.Token);
                        if (commandResult != null)
                        {
                            await _bus.PublishOutboundAsync(commandResult, _runningCts.Token);
                            continue;
                        }

                        // Process through message processor
                        var response = await _messageProcessor.ProcessMessageAsync(msg, _runningCts.Token);
                        if (response != null)
                        {
                            await _bus.PublishOutboundAsync(response, _runningCts.Token);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error processing message from {Channel}:{ChatId}", msg.Channel, msg.ChatId);

                        await _bus.PublishOutboundAsync(new OutboundMessage
                        {
                            Channel = msg.Channel,
                            ChatId = msg.ChatId,
                            Content = $"Sorry, I encountered an error: {ex.Message}"
                        }, _runningCts.Token);
                    }
                }
                catch (OperationCanceledException) when (_stopped)
                {
                    break;
                }
            }
        }
        finally
        {
            _logger?.LogInformation("Agent runtime stopped");
        }
    }

    public void Stop()
    {
        if (_stopped) return;

        _stopped = true;
        _runningCts?.Cancel();
        _logger?.LogInformation("Agent runtime stopping");
    }

    public async Task<string> ProcessDirectAsync(
        string content,
        string sessionKey = "chat_direct",
        string channel = "cli",
        string chatId = "direct",
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var session = await _sessionManager.GetOrCreateSessionAsync(sessionKey, cancellationToken);
        LogSessionDiagnostics(sessionKey, session, "before-direct");
        var msg = new InboundMessage
        {
            Channel = channel,
            SenderId = "user",
            ChatId = chatId,
            Content = content
        };

        var commandResult = await TryHandleCommandAsync(msg, cancellationToken);
        if (commandResult != null)
        {
            sw.Stop();
            _logger?.LogInformation("[TIMING] ProcessDirectAsync total: {ElapsedMs}ms", sw.ElapsedMilliseconds);
            return commandResult.Content ?? string.Empty;
        }

        var response = await _messageProcessor.ProcessMessageAsync(msg, cancellationToken, sessionKey);
        LogSessionDiagnostics(sessionKey, session, "after-direct");
        sw.Stop();
        _logger?.LogInformation("[TIMING] ProcessDirectAsync total: {ElapsedMs}ms", sw.ElapsedMilliseconds);
        return response?.Content ?? string.Empty;
    }

    public async IAsyncEnumerable<AgentResponseUpdate> ProcessDirectStreamingAsync(
        string content,
        string sessionKey = "chat_direct",
        string channel = "cli",
        string chatId = "direct",
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var session = await _sessionManager.GetOrCreateSessionAsync(sessionKey, cancellationToken);
        LogSessionDiagnostics(sessionKey, session, "before-streaming");
        var msg = new InboundMessage
        {
            Channel = channel,
            SenderId = "user",
            ChatId = chatId,
            Content = content
        };

        var commandResult = await TryHandleCommandAsync(msg, cancellationToken);
        if (commandResult != null)
        {
            yield return new AgentResponseUpdate(ChatRole.Assistant, commandResult.Content ?? string.Empty);
            yield break;
        }

        await foreach (var update in _streamingProcessor.ProcessDirectStreamingAsync(content, sessionKey, channel, cancellationToken))
        {
            yield return update;
        }

        LogSessionDiagnostics(sessionKey, session, "after-streaming");
    }

    public async Task<bool> TryCancelSessionAsync(string sessionKey)
    {
        var cancelled = false;

        if (_sessionTokens.TryGetValue(sessionKey, out var cts))
        {
            cts.Cancel();
            _logger?.LogInformation("Cancelled session {SessionKey}", sessionKey);
            cancelled = true;
        }

        // Cancel subagents for this session
        if (_serviceProvider?.GetService<ISubagentManager>() is { } subagentManager)
        {
            if (subagentManager.CancelSession(sessionKey))
            {
                _logger?.LogInformation("Cancelled subagents for session {SessionKey}", sessionKey);
                cancelled = true;
            }
        }

        if (!cancelled)
        {
            _logger?.LogDebug("No active session found for {SessionKey}", sessionKey);
        }

        await Task.Delay(0);
        return cancelled;
    }

    public void SetRuntimeMetadata(string sessionKey, IReadOnlyDictionary<string, string> metadata)
    {
        _runtimeMetadata[sessionKey] = metadata;
        _logger?.LogDebug("Set runtime metadata for session {SessionKey}", sessionKey);
    }

    private IReadOnlyDictionary<string, string>? GetRuntimeMetadata(string sessionKey)
    {
        return _runtimeMetadata.TryGetValue(sessionKey, out var metadata) ? metadata : null;
    }

    internal string? GetSelectedProfileIdForSession(string sessionKey)
    {
        var profileId = _sessionManager.GetSessionProfileId(sessionKey);
        if (string.IsNullOrEmpty(profileId))
        {
            return _llmConfig?.DefaultProfile;
        }

        return profileId;
    }

    private LlmProfile? GetEffectiveProfileForSession(string sessionKey)
    {
        if (_llmConfig == null)
        {
            return null;
        }

        var selectedProfileId = GetSelectedProfileIdForSession(sessionKey) ?? _llmConfig.DefaultProfile ?? "default";
        if (_llmConfig.Profiles.TryGetValue(selectedProfileId, out var selectedProfile))
        {
            return selectedProfile;
        }

        return _llmConfig.Profiles.TryGetValue("default", out var defaultProfile) ? defaultProfile : null;
    }

    internal int GetToolCountForSession(string sessionKey)
    {
        var agent = GetAgentForSession(sessionKey);
        return agent.GetOptions()?.ChatOptions?.Tools?.Count ?? 0;
    }

    private void LogSessionDiagnostics(string sessionKey, AgentSession? session, string phase)
    {
        if (_logger == null || !_logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        var selectedProfileId = GetSelectedProfileIdForSession(sessionKey) ?? _llmConfig?.DefaultProfile ?? "default";
        var effectiveProfile = GetEffectiveProfileForSession(sessionKey);
        var history = session?.GetAllMessages()?.ToList() ?? [];
        var historyCount = history.Count;
        var functionCallCount = history.SelectMany(m => m.Contents).Count(c => c is FunctionCallContent);
        var functionResultCount = history.SelectMany(m => m.Contents).Count(c => c is FunctionResultContent);
        var toolCount = GetToolCountForSession(sessionKey);

        _logger.LogInformation(
            "Agent runtime diagnostics ({Phase}) session={SessionKey} profile={ProfileId} provider={Provider} model={Model} apiBase={ApiBase} maxTokens={MaxTokens} tools={ToolCount} historyMessages={HistoryCount} functionCalls={FunctionCallCount} functionResults={FunctionResultCount}",
            phase,
            sessionKey,
            selectedProfileId,
            effectiveProfile?.Provider ?? "openai",
            effectiveProfile?.Model ?? "<unknown>",
            effectiveProfile?.ApiBase,
            effectiveProfile?.MaxTokens,
            toolCount,
            historyCount,
            functionCallCount,
            functionResultCount);
    }

    private async Task<OutboundMessage?> TryHandleCommandAsync(InboundMessage msg, CancellationToken cancellationToken)
    {
        var content = msg.Content.Trim();
        var commandName = content.StartsWith('/')
            ? content.Split(' ')[0].ToLowerInvariant()
            : content.ToLowerInvariant();

        if (!_commands.TryGetValue(commandName, out var command))
        {
            return null;
        }

        return await command.Handler(msg, cancellationToken);
    }

    /// <summary>
    /// Gets the ChatClientAgent for a session, considering profile settings.
    /// </summary>
    private ChatClientAgent GetAgentForSession(string sessionKey)
    {
        if (_chatClientFactory == null || _llmConfig == null)
        {
            return _defaultAgent;
        }

        var profileId = _sessionManager.GetSessionProfileId(sessionKey);
        if (string.IsNullOrEmpty(profileId))
        {
            _logger?.LogDebug("Using default agent for session {SessionKey} because no profile is bound", sessionKey);
            return _defaultAgent;
        }

        if (profileId.Equals(_llmConfig.DefaultProfile, StringComparison.OrdinalIgnoreCase) ||
            (string.IsNullOrEmpty(_llmConfig.DefaultProfile) && profileId == "default"))
        {
            _logger?.LogDebug("Using default agent for session {SessionKey} with profile {ProfileId}", sessionKey, profileId);
            return _defaultAgent;
        }

        _logger?.LogDebug("Using profile-specific agent for session {SessionKey} with profile {ProfileId}", sessionKey, profileId);
        return _profileAgents.GetOrAdd(profileId, _ => CreateAgentForProfile(profileId));
    }

    /// <summary>
    /// Creates a new ChatClientAgent for a specific profile.
    /// </summary>
    private ChatClientAgent CreateAgentForProfile(string profileId)
    {
        _logger?.LogInformation("Creating ChatClientAgent for profile: {ProfileId}", profileId);

        if (_llmConfig == null || _chatClientFactory == null || _serviceProvider == null)
        {
            _logger?.LogWarning("Required services not available for profile {ProfileId}, using default agent", profileId);
            return _defaultAgent;
        }

        if (!_llmConfig.Profiles.TryGetValue(profileId, out var profile))
        {
            _logger?.LogWarning("Profile {ProfileId} not found, using default agent", profileId);
            return _defaultAgent;
        }

        try
        {
            var chatClient = _chatClientFactory.CreateChatClient(
                profile.Provider ?? "openai",
                profile.Model,
                profile.ApiKey,
                profile.ApiBase,
                profile.MaxTokens);

            var workspace = _serviceProvider.GetRequiredService<IWorkspaceManager>();
            var skillsLoader = _serviceProvider.GetRequiredService<ISkillsLoader>();
            var memoryStore = _serviceProvider.GetService<IMemoryStore>();
            var loggerFactory = _serviceProvider.GetService<ILoggerFactory>();
            var agentConfig = _serviceProvider.GetService<AgentConfig>();
            var tools = _serviceProvider.GetServices<AITool>().ToList();
            var memoryWindow = agentConfig?.Memory?.MemoryWindow ?? 50;
            var maxInstructionChars = agentConfig?.Memory?.MaxInstructionChars ?? 0;

            var agentOptions = new AgentOptions
            {
                Temperature = (float)profile.Temperature,
                MaxTokens = profile.MaxTokens
            };

            _logger?.LogInformation(
                "Creating profile agent details profile={ProfileId} provider={Provider} model={Model} apiBase={ApiBase} maxTokens={MaxTokens} tools={ToolCount} memoryWindow={MemoryWindow} maxInstructionChars={MaxInstructionChars}",
                profileId,
                profile.Provider ?? "openai",
                profile.Model,
                profile.ApiBase,
                profile.MaxTokens,
                tools.Count,
                memoryWindow,
                maxInstructionChars);

            return NanoBotAgentFactory.Create(
                chatClient,
                workspace,
                skillsLoader,
                tools,
                loggerFactory,
                agentOptions,
                memoryStore,
                memoryWindow,
                maxInstructionChars,
                agentConfig?.Timezone);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to create agent for profile {ProfileId}, using default agent", profileId);
            return _defaultAgent;
        }
    }

    public void ClearAgentCache()
    {
        _profileAgents.Clear();
        _logger?.LogInformation("Cleared cached profile agents");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();

        foreach (var cts in _sessionTokens.Values)
        {
            cts.Dispose();
        }

        _sessionTokens.Clear();
        ClearAgentCache();
        _runningCts?.Dispose();
    }
}
