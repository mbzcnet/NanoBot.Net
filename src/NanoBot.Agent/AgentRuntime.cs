using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Bus;

namespace NanoBot.Agent;

public interface IAgentRuntime
{
    Task RunAsync(CancellationToken cancellationToken = default);
    void Stop();
    Task<string> ProcessDirectAsync(string content, string sessionKey = "cli:direct", string channel = "cli", string chatId = "direct", CancellationToken cancellationToken = default);
}

public sealed class AgentRuntime : IAgentRuntime, IDisposable
{
    private readonly ChatClientAgent _agent;
    private readonly IMessageBus _bus;
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<AgentRuntime>? _logger;
    private readonly string _sessionsDirectory;
    private CancellationTokenSource? _runningCts;
    private bool _disposed;
    private bool _stopped;

    public AgentRuntime(
        ChatClientAgent agent,
        IMessageBus bus,
        ISessionManager sessionManager,
        string sessionsDirectory,
        ILogger<AgentRuntime>? logger = null)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _sessionsDirectory = sessionsDirectory;
        _logger = logger;

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
        var msg = new InboundMessage
        {
            Channel = channel,
            SenderId = "user",
            ChatId = chatId,
            Content = content
        };

        var response = await ProcessMessageAsync(msg, cancellationToken, sessionKey);
        return response?.Content ?? string.Empty;
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
        switch (cmd)
        {
            case "/new":
                return await HandleNewSessionCommandAsync(msg, cancellationToken);
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
        CancellationToken cancellationToken)
    {
        var sessionKey = msg.SessionKey;
        await _sessionManager.ClearSessionAsync(sessionKey, cancellationToken);

        return new OutboundMessage
        {
            Channel = msg.Channel,
            ChatId = msg.ChatId,
            Content = "New session started."
        };
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
