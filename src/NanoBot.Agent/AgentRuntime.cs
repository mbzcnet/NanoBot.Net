using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Bus;
using NanoBot.Core.Memory;
using NanoBot.Core.Workspace;
using NanoBot.Infrastructure.Memory;

namespace NanoBot.Agent;

public interface IAgentRuntime
{
    Task RunAsync(CancellationToken cancellationToken = default);
    void Stop();
    Task<string> ProcessDirectAsync(string content, string sessionKey = "cli:direct", string channel = "cli", string chatId = "direct", CancellationToken cancellationToken = default);
    IAsyncEnumerable<AgentResponseUpdate> ProcessDirectStreamingAsync(string content, string sessionKey = "cli:direct", string channel = "cli", string chatId = "direct", CancellationToken cancellationToken = default);
}

public sealed class AgentRuntime : IAgentRuntime, IDisposable
{
    private readonly ChatClientAgent _agent;
    private readonly IMessageBus _bus;
    private readonly ISessionManager _sessionManager;
    private readonly IWorkspaceManager _workspace;
    private readonly IMemoryStore? _memoryStore;
    private readonly ILogger<AgentRuntime>? _logger;
    private readonly string _sessionsDirectory;
    private readonly int _memoryWindow;
    private CancellationTokenSource? _runningCts;
    private bool _disposed;
    private bool _stopped;

    public AgentRuntime(
        ChatClientAgent agent,
        IMessageBus bus,
        ISessionManager sessionManager,
        IWorkspaceManager workspace,
        IMemoryStore? memoryStore,
        int memoryWindow,
        ILogger<AgentRuntime>? logger = null)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _memoryStore = memoryStore;
        _memoryWindow = memoryWindow;
        _logger = logger;
        _sessionsDirectory = _workspace.GetSessionsPath();

        if (!Directory.Exists(_sessionsDirectory))
        {
            Directory.CreateDirectory(_sessionsDirectory);
        }
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

        sw.Restart();
        var userMessage = new ChatMessage(ChatRole.User, content);
        _logger?.LogInformation("[TIMING] Create user message: {ElapsedMs}ms", sw.ElapsedMilliseconds);

        sw.Restart();
        _logger?.LogInformation("[TIMING] About to call _agent.RunStreamingAsync...");
        
        var swInner = System.Diagnostics.Stopwatch.StartNew();
        var firstChunkReceived = false;
        await foreach (var update in _agent.RunStreamingAsync([userMessage], session, cancellationToken: cancellationToken))
        {
            swInner.Stop(); // Stop timing for each chunk
            if (!firstChunkReceived)
            {
                firstChunkReceived = true;
                _logger?.LogInformation("[TIMING] ★★★ FIRST CHUNK from _agent.RunStreamingAsync: {ElapsedMs}ms ★★★", swInner.ElapsedMilliseconds);
            }
            else
            {
                _logger?.LogInformation("[TIMING] Subsequent chunk: {ElapsedMs}ms, text: {Text}", swInner.ElapsedMilliseconds, update.Text?.Length > 50 ? update.Text[..50] + "..." : update.Text);
            }
            
            // Check for tool calls and send tool hint if text is empty
            var functionCalls = update.Contents.OfType<FunctionCallContent>().ToList();
            if (functionCalls.Any() && string.IsNullOrWhiteSpace(update.Text))
            {
                var toolHint = ToolHintFormatter.FormatToolHint(functionCalls);
                if (!string.IsNullOrEmpty(toolHint))
                {
                    // Create a tool hint update with metadata
                    var toolHintUpdate = new AgentResponseUpdate
                    {
                        Role = ChatRole.Assistant,
                        Contents = { new TextContent(toolHint) }
                    };
                    toolHintUpdate.AdditionalProperties["_tool_hint"] = true;
                    yield return toolHintUpdate;
                }
            }
            
            swInner.Restart(); // Restart for next chunk
            yield return update;
        }
        sw.Stop();
        _logger?.LogInformation("[TIMING] RunStreamingAsync completed: {ElapsedMs}ms", sw.ElapsedMilliseconds);
        
        _ = TryConsolidateMemoryAsync(session, sessionKey, CancellationToken.None).ContinueWith(t => 
        {
            if (t.IsFaulted)
                _logger?.LogWarning(t.Exception, "Background memory consolidation failed");
        }, TaskContinuationOptions.OnlyOnFaulted);
        
        swTotal.Stop();
        _logger?.LogInformation("[TIMING] ProcessDirectStreamingAsync total: {ElapsedMs}ms", swTotal.ElapsedMilliseconds);
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

        var cmd = msg.Content.Trim().ToLowerInvariant();
        if (cmd == "/new")
        {
            var existingSession = await _sessionManager.GetOrCreateSessionAsync(sessionKey, cancellationToken);
            return await HandleNewSessionCommandAsync(msg, existingSession, cancellationToken);
        }
        switch (cmd)
        {
            case "/help":
                return new OutboundMessage
                {
                    Channel = msg.Channel,
                    ChatId = msg.ChatId,
                    Content = "🐈 nanobot commands:\n/new — Start a new conversation\n/help — Show available commands"
                };
        }

        var session = await _sessionManager.GetOrCreateSessionAsync(sessionKey, cancellationToken);

        var userMessage = new ChatMessage(ChatRole.User, msg.Content);

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
            response = await _agent.RunAsync([userMessage], session, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Agent run failed for session {SessionKey}", sessionKey);
            throw;
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

    private IChatClient? GetChatClientFromAgent()
    {
        var field = _agent.GetType().GetField("_chatClient", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return field?.GetValue(_agent) as IChatClient;
    }

    private List<ChatMessage> GetSessionMessages(AgentSession session)
    {
        var messages = new List<ChatMessage>();
        
        var historyProvider = session.GetService<ChatHistoryProvider>();
        if (historyProvider != null)
        {
            var method = typeof(ChatHistoryProvider).GetMethod("GetAllMessages", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (method != null)
            {
                var result = method.Invoke(historyProvider, null);
                if (result is IEnumerable<ChatMessage> enumerable)
                {
                    messages.AddRange(enumerable);
                }
            }
        }

        return messages;
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

    public void Dispose()
    {
        if (_disposed) return;

        Stop();
        _runningCts?.Dispose();
        _disposed = true;

        _logger?.LogInformation("Agent runtime disposed");
    }
}
