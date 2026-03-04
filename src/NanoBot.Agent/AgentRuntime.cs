using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NanoBot.Agent.Extensions;
using NanoBot.Core.Bus;
using NanoBot.Core.Memory;
using NanoBot.Core.Subagents;
using NanoBot.Core.Workspace;
using NanoBot.Infrastructure.Memory;

namespace NanoBot.Agent;

public interface IAgentRuntime
{
    Task RunAsync(CancellationToken cancellationToken = default);
    void Stop();
    Task<string> ProcessDirectAsync(string content, string sessionKey = "cli:direct", string channel = "cli", string chatId = "direct", CancellationToken cancellationToken = default);
    IAsyncEnumerable<AgentResponseUpdate> ProcessDirectStreamingAsync(string content, string sessionKey = "cli:direct", string channel = "cli", string chatId = "direct", CancellationToken cancellationToken = default);
    Task<bool> TryCancelSessionAsync(string sessionKey);
    void SetRuntimeMetadata(string sessionKey, IReadOnlyDictionary<string, string> metadata);
}

public sealed class AgentRuntime : IAgentRuntime, IDisposable
{
    private readonly ChatClientAgent _agent;
    private readonly IMessageBus _bus;
    private readonly ISessionManager _sessionManager;
    private readonly IWorkspaceManager _workspace;
    private readonly IMemoryStore? _memoryStore;
    private readonly ISubagentManager? _subagentManager;
    private readonly ILogger<AgentRuntime>? _logger;
    private readonly string _sessionsDirectory;
    private readonly int _memoryWindow;
    private CancellationTokenSource? _runningCts;
    private bool _disposed;
    private bool _stopped;

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
        ILogger<AgentRuntime>? logger = null)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _memoryStore = memoryStore;
        _subagentManager = subagentManager;
        _memoryWindow = memoryWindow;
        _logger = logger;
        _sessionsDirectory = _workspace.GetSessionsPath();

        if (!Directory.Exists(_sessionsDirectory))
        {
            Directory.CreateDirectory(_sessionsDirectory);
        }

        // Register built-in commands
        RegisterCommand(new CommandDefinition(
            Name: "/new",
            Description: "Start a new conversation",
            Immediate: false,
            Handler: async (msg, ct) =>
            {
                var existingSession = await _sessionManager.GetOrCreateSessionAsync(msg.SessionKey, ct);
                return await HandleNewSessionCommandAsync(msg, existingSession, ct);
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
            Handler: async (msg, ct) =>
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
        var sb = new StringBuilder("🐈 nanobot commands:\n");
        foreach (var cmd in _commands.Values.OrderBy(c => c.Name))
        {
            sb.AppendLine($"{cmd.Name} — {cmd.Description}");
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
                        var response = await ProcessMessageAsync(msg, _runningCts.Token);
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
        string sessionKey = "cli:direct",
        string channel = "cli",
        string chatId = "direct",
        CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var msg = new InboundMessage
        {
            Channel = channel,
            SenderId = "user",
            ChatId = chatId,
            Content = content
        };

        var response = await ProcessMessageAsync(msg, cancellationToken, sessionKey);
        sw.Stop();
        _logger?.LogInformation("[TIMING] ProcessDirectAsync total: {ElapsedMs}ms", sw.ElapsedMilliseconds);
        return response?.Content ?? string.Empty;
    }

    public async IAsyncEnumerable<AgentResponseUpdate> ProcessDirectStreamingAsync(
        string content,
        string sessionKey = "cli:direct",
        string channel = "cli",
        string chatId = "direct",
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var swTotal = System.Diagnostics.Stopwatch.StartNew();
        var preview = content.Length > 80 ? content[..80] + "..." : content;
        _logger?.LogInformation("[TIMING] Starting streaming request from {Channel}: {Preview}", channel, preview);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var session = await _sessionManager.GetOrCreateSessionAsync(sessionKey, cancellationToken);
        sw.Stop();
        _logger?.LogInformation("[TIMING] GetOrCreateSessionAsync: {ElapsedMs}ms", sw.ElapsedMilliseconds);

        // Create CancellationTokenSource for this session
        var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _sessionTokens.AddOrUpdate(sessionKey, _ => sessionCts, (_, _) => sessionCts);

        sw.Restart();
        var userMessage = new ChatMessage(ChatRole.User, content);
        userMessage = userMessage.WithAgentRequestMessageSource(AgentRequestMessageSourceType.External, "user");
        _logger?.LogInformation("[DEBUG] Created user message with content length: {Length}, source type: External", content.Length);
        _logger?.LogInformation("[TIMING] Create user message: {ElapsedMs}ms", sw.ElapsedMilliseconds);

        // 自动提取标题：如果是第一条用户消息，使用消息内容作为标题
        await TryAutoSetSessionTitleAsync(session, sessionKey, content, cancellationToken);

        sw.Restart();
        _logger?.LogInformation("[TIMING] About to call _agent.RunStreamingAsync...");

        await foreach (var update in StreamWithToolHintsAsync(session, userMessage, sessionCts.Token))
        {
            yield return update;
        }

        _sessionTokens.TryRemove(sessionKey, out _);
        sessionCts.Dispose();

        sw.Stop();
        _logger?.LogInformation("[TIMING] RunStreamingAsync completed: {ElapsedMs}ms", sw.ElapsedMilliseconds);

        // 保存会话到文件
        try
        {
            await _sessionManager.SaveSessionAsync(session, sessionKey, cancellationToken);
            _logger?.LogInformation("[TIMING] Session saved after streaming");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save session after streaming for {SessionKey}", sessionKey);
        }

        _ = TryConsolidateMemoryAsync(session, sessionKey, CancellationToken.None).ContinueWith(t =>
        {
            if (t.IsFaulted)
                _logger?.LogWarning(t.Exception, "Background memory consolidation failed");
        }, TaskContinuationOptions.OnlyOnFaulted);

        swTotal.Stop();
        _logger?.LogInformation("[TIMING] ProcessDirectStreamingAsync total: {ElapsedMs}ms", swTotal.ElapsedMilliseconds);
    }

    private async IAsyncEnumerable<AgentResponseUpdate> StreamWithToolHintsAsync(
        AgentSession session,
        ChatMessage userMessage,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var swInner = System.Diagnostics.Stopwatch.StartNew();
        var firstChunkReceived = false;

        await foreach (var update in _agent.RunStreamingAsync([userMessage], session, cancellationToken: cancellationToken))
        {
            swInner.Stop();
            if (!firstChunkReceived)
            {
                firstChunkReceived = true;
                _logger?.LogInformation("[TIMING] ★★★ FIRST CHUNK from _agent.RunStreamingAsync: {ElapsedMs}ms ★★★", swInner.ElapsedMilliseconds);
            }
            else
            {
                _logger?.LogInformation("[TIMING] Subsequent chunk: {ElapsedMs}ms, text: {Text}", swInner.ElapsedMilliseconds, update.Text?.Length > 50 ? update.Text[..50] + "..." : update.Text);
            }

            var functionCalls = update.Contents.OfType<FunctionCallContent>().ToList();
            if (functionCalls.Any())
            {
                var toolHint = ToolHintFormatter.FormatToolHint(functionCalls);
                if (!string.IsNullOrEmpty(toolHint))
                {
                    var toolHintUpdate = new AgentResponseUpdate
                    {
                        Role = ChatRole.Assistant,
                        Contents = { new TextContent(toolHint) },
                        AdditionalProperties = new()
                    };
                    toolHintUpdate.AdditionalProperties["_tool_hint"] = true;
                    yield return toolHintUpdate;
                }
            }

            swInner.Restart();
            yield return update;
        }

        swInner.Stop();
        _logger?.LogInformation("[TIMING] RunStreamingAsync completed: {ElapsedMs}ms", swInner.ElapsedMilliseconds);
    }

    private async Task<OutboundMessage?> ProcessMessageAsync(
        InboundMessage msg,
        CancellationToken cancellationToken,
        string? overrideSessionKey = null)
    {
        var preview = msg.Content.Length > 80 ? msg.Content[..80] + "..." : msg.Content;
        _logger?.LogInformation("Processing message from {Channel}:{SenderId}: {Preview}", msg.Channel, msg.SenderId, preview);

        var sessionKey = overrideSessionKey ?? msg.SessionKey;

        if (msg.Channel == "system")
        {
            return await ProcessSystemMessageAsync(msg, cancellationToken);
        }

        // Try to handle as a command
        var commandResult = await TryHandleCommandAsync(msg, cancellationToken);
        if (commandResult != null)
        {
            return commandResult;
        }

        var session = await _sessionManager.GetOrCreateSessionAsync(sessionKey, cancellationToken);

        // Set runtime metadata in session state
        if (_runtimeMetadata.TryGetValue(sessionKey, out var metadata))
        {
            session.StateBag.SetValue("runtime:untrusted", JsonSerializer.Serialize(metadata));
        }

        // Create or reuse CancellationTokenSource for this session
        var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _sessionTokens.AddOrUpdate(sessionKey, _ => sessionCts, (_, _) => sessionCts);

        var userMessage = new ChatMessage(ChatRole.User, msg.Content);
        userMessage = userMessage.WithAgentRequestMessageSource(AgentRequestMessageSourceType.External, "user");

        // 自动提取标题：如果是第一条用户消息，使用消息内容作为标题
        await TryAutoSetSessionTitleAsync(session, sessionKey, msg.Content, cancellationToken);

        if (msg.Media != null && msg.Media.Count > 0)
        {
            var contents = new List<AIContent> { new TextContent(msg.Content) };
            foreach (var mediaPath in msg.Media)
            {
                contents.Add(new TextContent($"[Media: {mediaPath}]"));
            }
            userMessage = new ChatMessage(ChatRole.User, contents);
        }

        AgentResponse response;
        try
        {
            response = await _agent.RunAsync([userMessage], session, cancellationToken: sessionCts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("Session {SessionKey} was cancelled", sessionKey);
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Agent run failed for session {SessionKey}", sessionKey);
            throw;
        }
        finally
        {
            // Clean up session token
            _sessionTokens.TryRemove(sessionKey, out _);
            sessionCts.Dispose();
        }

        var responseText = response.Messages.FirstOrDefault()?.Text ?? "I've completed processing but have no response to give.";

        preview = responseText.Length > 120 ? responseText[..120] + "..." : responseText;
        _logger?.LogInformation("Response to {Channel}:{SenderId}: {Preview}", msg.Channel, msg.SenderId, preview);

        await _sessionManager.SaveSessionAsync(session, sessionKey, cancellationToken);

        await TryConsolidateMemoryAsync(session, sessionKey, cancellationToken);

        return new OutboundMessage
        {
            Channel = msg.Channel,
            ChatId = msg.ChatId,
            Content = responseText,
            Metadata = msg.Metadata
        };
    }

    private async Task<OutboundMessage?> ProcessSystemMessageAsync(
        InboundMessage msg,
        CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Processing system message from {SenderId}", msg.SenderId);

        string originChannel;
        string originChatId;

        if (msg.ChatId.Contains(':'))
        {
            var parts = msg.ChatId.Split(':', 2);
            originChannel = parts[0];
            originChatId = parts[1];
        }
        else
        {
            originChannel = "cli";
            originChatId = msg.ChatId;
        }

        var sessionKey = $"{originChannel}:{originChatId}";
        var session = await _sessionManager.GetOrCreateSessionAsync(sessionKey, cancellationToken);

        var systemMessage = new ChatMessage(ChatRole.User, $"[System: {msg.SenderId}] {msg.Content}");

        AgentResponse response;
        try
        {
            response = await _agent.RunAsync([systemMessage], session, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Agent run failed for system message");
            throw;
        }

        var responseText = response.Messages.FirstOrDefault()?.Text ?? "Background task completed.";

        await _sessionManager.SaveSessionAsync(session, sessionKey, cancellationToken);

        return new OutboundMessage
        {
            Channel = originChannel,
            ChatId = originChatId,
            Content = responseText
        };
    }

    private async Task<OutboundMessage> HandleNewSessionCommandAsync(
        InboundMessage msg,
        AgentSession existingSession,
        CancellationToken cancellationToken)
    {
        var sessionKey = msg.SessionKey;

        if (_memoryStore != null)
        {
            try
            {
                var chatClient = GetChatClientFromAgent();
                if (chatClient != null)
                {
                    var consolidator = new MemoryConsolidator(
                        chatClient,
                        _memoryStore,
                        _workspace,
                        _memoryWindow,
                        null);

                    var messages = GetSessionMessages(existingSession);
                    await consolidator.ConsolidateAsync(messages, 0, archiveAll: true, cancellationToken);
                    _logger?.LogInformation("Memory consolidation completed for /new command");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to consolidate memory for /new command");
            }
        }

        await _sessionManager.ClearSessionAsync(sessionKey, cancellationToken);

        return new OutboundMessage
        {
            Channel = msg.Channel,
            ChatId = msg.ChatId,
            Content = "New session started."
        };
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

        // Immediate commands are handled without agent processing
        if (command.Immediate)
        {
            return await command.Handler(msg, cancellationToken);
        }

        // Non-immediate commands (like /new) are also handled directly
        // This is for backward compatibility - /new needs session access
        return await command.Handler(msg, cancellationToken);
    }

    public async Task<bool> TryCancelSessionAsync(string sessionKey)
    {
        var cancelled = false;

        // Cancel the agent task
        if (_sessionTokens.TryGetValue(sessionKey, out var cts))
        {
            cts.Cancel();
            _logger?.LogInformation("Cancelled session {SessionKey}", sessionKey);
            cancelled = true;
        }

        // Cancel subagents for this session
        if (_subagentManager != null)
        {
            if (_subagentManager.CancelSession(sessionKey))
            {
                _logger?.LogInformation("Cancelled subagents for session {SessionKey}", sessionKey);
                cancelled = true;
            }
        }

        if (!cancelled)
        {
            _logger?.LogDebug("No active session found for {SessionKey}", sessionKey);
        }

        await Task.Delay(0); // Allow cancellation to propagate
        return cancelled;
    }

    public void SetRuntimeMetadata(string sessionKey, IReadOnlyDictionary<string, string> metadata)
    {
        _runtimeMetadata[sessionKey] = metadata;
        _logger?.LogDebug("Set runtime metadata for session {SessionKey}", sessionKey);
    }

    public IReadOnlyDictionary<string, string>? GetRuntimeMetadata(string sessionKey)
    {
        return _runtimeMetadata.TryGetValue(sessionKey, out var metadata) ? metadata : null;
    }

    private IChatClient? GetChatClientFromAgent()
    {
        return _agent.GetChatClient();
    }

    private List<ChatMessage> GetSessionMessages(AgentSession session)
    {
        return session.GetAllMessages().ToList();
    }

    private async Task TryConsolidateMemoryAsync(AgentSession session, string sessionKey, CancellationToken cancellationToken)
    {
        if (_memoryStore == null)
            return;

        try
        {
            var messages = GetSessionMessages(session);
            if (messages.Count <= _memoryWindow)
            {
                _logger?.LogDebug("Memory consolidation skipped: {Count} messages <= window {Window}", messages.Count, _memoryWindow);
                return;
            }

            var chatClient = GetChatClientFromAgent();
            if (chatClient == null)
            {
                _logger?.LogWarning("Could not get ChatClient for memory consolidation");
                return;
            }

            var consolidator = new MemoryConsolidator(
                chatClient,
                _memoryStore,
                _workspace,
                _memoryWindow);

            _logger?.LogInformation("Starting memory consolidation for {Count} messages", messages.Count);
            var lastConsolidated = _sessionManager.GetLastConsolidated(sessionKey);
            var newLastConsolidated = await consolidator.ConsolidateAsync(messages, lastConsolidated, archiveAll: false, cancellationToken);

            if (newLastConsolidated.HasValue)
            {
                _sessionManager.SetLastConsolidated(sessionKey, newLastConsolidated.Value);
                await _sessionManager.SaveSessionAsync(session, sessionKey, cancellationToken);
            }
            _logger?.LogInformation("Memory consolidation completed");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Memory consolidation failed");
        }
    }

    private async Task TryAutoSetSessionTitleAsync(AgentSession session, string sessionKey, string userContent, CancellationToken cancellationToken)
    {
        try
        {
            // 只处理 webui 会话
            if (!sessionKey.StartsWith("webui:"))
                return;

            // 获取当前标题
            var currentTitle = _sessionManager.GetSessionTitle(sessionKey);

            // 如果标题已自定义（不是默认格式），则不自动更新
            if (!string.IsNullOrEmpty(currentTitle) && !currentTitle.StartsWith("会话 "))
                return;

            // 获取会话中的消息数量
            var messages = GetSessionMessages(session);

            // 如果这是第一条用户消息（session 中还没有消息），则使用内容作为标题
            if (messages.Count == 0)
            {
                var newTitle = userContent.Length > 30 ? userContent.Substring(0, 30) + "..." : userContent;
                if (!string.IsNullOrWhiteSpace(newTitle))
                {
                    _sessionManager.SetSessionTitle(sessionKey, newTitle);
                    // 立即保存以更新文件中的标题
                    await _sessionManager.SaveSessionAsync(session, sessionKey, cancellationToken);
                    _logger?.LogInformation("Auto-set session title for {SessionKey} to: {Title}", sessionKey, newTitle);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to auto-set session title for {SessionKey}", sessionKey);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        Stop();
        _runningCts?.Dispose();
        _disposed = true;

        _logger?.LogInformation("Agent runtime disposed");
    }
}
