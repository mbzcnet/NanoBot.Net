using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Agents.AI;
using NanoBot.Core.Bus;
using NanoBot.Core.Tools;

namespace NanoBot.Agent.Services;

/// <summary>
/// Handles non-streaming message processing.
/// </summary>
public sealed class MessageProcessor
{
    private readonly ChatClientAgent _defaultAgent;
    private readonly ISessionManager _sessionManager;
    private readonly MemoryConsolidationService _memoryConsolidationService;
    private readonly SessionTitleManager _sessionTitleManager;
    private readonly ImageContentProcessor _imageProcessor;
    private readonly ILogger<MessageProcessor>? _logger;
    private readonly Func<string, ChatClientAgent> _getAgentForSession;
    private readonly Func<string, IReadOnlyDictionary<string, string>>? _getRuntimeMetadata;
    private readonly Func<string, IChatClient?> _getChatClient;

    public MessageProcessor(
        ChatClientAgent defaultAgent,
        ISessionManager sessionManager,
        MemoryConsolidationService memoryConsolidationService,
        SessionTitleManager sessionTitleManager,
        ImageContentProcessor imageProcessor,
        Func<string, ChatClientAgent> getAgentForSession,
        Func<string, IChatClient?> getChatClient,
        Func<string, IReadOnlyDictionary<string, string>>? getRuntimeMetadata = null,
        ILogger<MessageProcessor>? logger = null)
    {
        _defaultAgent = defaultAgent;
        _sessionManager = sessionManager;
        _memoryConsolidationService = memoryConsolidationService;
        _sessionTitleManager = sessionTitleManager;
        _imageProcessor = imageProcessor;
        _getAgentForSession = getAgentForSession;
        _getChatClient = getChatClient;
        _getRuntimeMetadata = getRuntimeMetadata;
        _logger = logger;
    }

    /// <summary>
    /// Processes a non-streaming inbound message.
    /// </summary>
    public async Task<OutboundMessage?> ProcessMessageAsync(
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

        var session = await _sessionManager.GetOrCreateSessionAsync(sessionKey, cancellationToken);

        // Set runtime metadata in session state
        if (_getRuntimeMetadata?.Invoke(sessionKey) is { } metadata)
        {
            session.StateBag.SetValue("runtime:untrusted", JsonSerializer.Serialize(metadata));
        }

        var imageUrls = msg.Media?.Where(static m => !string.IsNullOrWhiteSpace(m)).ToArray();
        var userMessage = _imageProcessor.BuildUserMessage(msg.Content, imageUrls);
        userMessage = userMessage.WithAgentRequestMessageSource(AgentRequestMessageSourceType.External, "user");

        // Auto-set session title if this is the first message
        await _sessionTitleManager.TryAutoSetSessionTitleAsync(session, sessionKey, msg.Content, cancellationToken);

        AgentResponse response;
        ToolExecutionContext.SetCurrentSessionKey(sessionKey);
        try
        {
            var agent = _getAgentForSession(sessionKey);
            response = await agent.RunAsync([userMessage], session, cancellationToken: cancellationToken);
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
            ToolExecutionContext.SetCurrentSessionKey(null);
        }

        var responseText = response.Text;
        if (string.IsNullOrWhiteSpace(responseText))
        {
            responseText = "I've completed processing but have no response to give.";
        }

        // Extract snapshot images from response
        var snapshotImageContexts = _imageProcessor.ExtractSnapshotImageContext(response.Messages.SelectMany(m => m.Contents));
        var messageMetadata = new Dictionary<string, object>();
        if (msg.Metadata != null)
        {
            foreach (var kvp in msg.Metadata)
            {
                messageMetadata[kvp.Key] = kvp.Value;
            }
        }
        if (snapshotImageContexts != null && snapshotImageContexts.Length > 0)
        {
            _logger?.LogInformation("Snapshot images extracted for non-streaming response session {SessionKey}: {Count} images", sessionKey, snapshotImageContexts.Length);
            messageMetadata["_snapshot_images"] = snapshotImageContexts;
        }

        preview = responseText.Length > 120 ? responseText[..120] + "..." : responseText;
        _logger?.LogInformation("Response to {Channel}:{SenderId}: {Preview}", msg.Channel, msg.SenderId, preview);

        await _sessionManager.SaveSessionAsync(session, sessionKey, cancellationToken);

        await _memoryConsolidationService.TryConsolidateAsync(session, sessionKey, cancellationToken);

        return new OutboundMessage
        {
            Channel = msg.Channel,
            ChatId = msg.ChatId,
            Content = responseText,
            Metadata = messageMetadata
        };
    }

    /// <summary>
    /// Processes a system message (background tasks).
    /// </summary>
    public async Task<OutboundMessage> ProcessSystemMessageAsync(
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
            var agent = _getAgentForSession(sessionKey);
            response = await agent.RunAsync([systemMessage], session, cancellationToken: cancellationToken);
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

    /// <summary>
    /// Handles the /new command to start a new session.
    /// </summary>
    public async Task<OutboundMessage> HandleNewSessionCommandAsync(
        InboundMessage msg,
        AgentSession existingSession,
        CancellationToken cancellationToken)
    {
        var sessionKey = msg.SessionKey;

        // Consolidate all existing messages to memory
        await _memoryConsolidationService.ConsolidateAllAsync(existingSession, sessionKey, cancellationToken);

        // Clear the session
        await _sessionManager.ClearSessionAsync(sessionKey, cancellationToken);

        return new OutboundMessage
        {
            Channel = msg.Channel,
            ChatId = msg.ChatId,
            Content = "New session started."
        };
    }
}
