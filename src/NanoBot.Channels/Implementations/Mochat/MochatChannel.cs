using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using NanoBot.Channels.Abstractions;
using NanoBot.Core.Bus;
using NanoBot.Core.Configuration;

namespace NanoBot.Channels.Implementations.Mochat;

public class MochatChannel : ChannelBase
{
    public override string Id => "mochat";
    public override string Type => "mochat";

    private readonly MochatConfig _config;
    private readonly HttpClient _httpClient;
    private ClientWebSocket? _webSocket;
    private readonly ConcurrentDictionary<string, DateTime> _lastMessageTime = new();
    private readonly ConcurrentDictionary<string, string> _pendingReplies = new();

    public MochatChannel(MochatConfig config, IMessageBus bus, ILogger<MochatChannel> logger)
        : base(bus, logger)
    {
        _config = config;
        _httpClient = new HttpClient();
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_config.ClawToken))
        {
            _logger.LogError("Mochat claw_token not configured");
            return;
        }

        _running = true;

        try
        {
            var socketUrl = string.IsNullOrEmpty(_config.SocketUrl)
                ? $"{_config.BaseUrl.Replace("https://", "wss://").Replace("http://", "ws://")}/socket.io/?EIO=4&transport=websocket"
                : _config.SocketUrl;

            _webSocket = new ClientWebSocket();
            await _webSocket.ConnectAsync(new Uri(socketUrl), cancellationToken);

            _logger.LogInformation("Mochat channel started");

            _ = ReceiveMessagesAsync(cancellationToken);

            if (_config.Sessions.Count > 0)
            {
                await SubscribeSessionsAsync(_config.Sessions);
            }

            if (_config.Panels.Count > 0)
            {
                await SubscribePanelsAsync(_config.Panels);
            }

            while (_running && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Mochat channel");
        }
    }

    private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];

        while (_webSocket?.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", cancellationToken);
                    break;
                }

                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);

                if (message.StartsWith("42"))
                {
                    var jsonStart = message.IndexOf('[');
                    if (jsonStart >= 0)
                    {
                        var json = message[jsonStart..];
                        var array = JsonSerializer.Deserialize<JsonArray>(json);

                        if (array != null && array.Count >= 2)
                        {
                            var eventName = array[0]?.ToString();
                            var data = array[1];

                            if (eventName == "claw.session.events" || eventName == "claw.panel.events")
                            {
                                await HandleEventAsync(data, cancellationToken);
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error receiving Mochat message");
            }
        }
    }

    private async Task HandleEventAsync(JsonNode? data, CancellationToken cancellationToken)
    {
        if (data == null) return;

        var senderId = data["sender_id"]?.ToString();
        var sessionId = data["session_id"]?.ToString();
        var content = data["content"]?.ToString() ?? "";
        var panelId = data["panel_id"]?.ToString();

        var chatId = !string.IsNullOrEmpty(sessionId) ? sessionId : panelId;

        if (string.IsNullOrEmpty(senderId) || string.IsNullOrEmpty(chatId))
            return;

        if (!IsAllowed(senderId, _config.AllowFrom))
            return;

        if (_config.Mention.RequireInGroups && !string.IsNullOrEmpty(panelId))
        {
            var mentionRequired = !content.Contains($"@{_config.AgentUserId}");
            if (mentionRequired)
                return;
        }

        await HandleMessageAsync(
            senderId,
            chatId,
            content,
            Array.Empty<string>(),
            new Dictionary<string, object>
            {
                ["session_id"] = sessionId ?? "",
                ["panel_id"] = panelId ?? ""
            }
        );
    }

    public async Task SubscribeSessionsAsync(IReadOnlyList<string> sessionIds)
    {
        if (_webSocket?.State != WebSocketState.Open)
            return;

        var payload = $"42[\"claw.sessions.subscribe\",{JsonSerializer.Serialize(sessionIds)}]";
        var bytes = Encoding.UTF8.GetBytes(payload);
        await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    public async Task SubscribePanelsAsync(IReadOnlyList<string> panelIds)
    {
        if (_webSocket?.State != WebSocketState.Open)
            return;

        var payload = $"42[\"claw.panels.subscribe\",{JsonSerializer.Serialize(panelIds)}]";
        var bytes = Encoding.UTF8.GetBytes(payload);
        await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    public async Task RefreshTargetsAsync()
    {
        if (_webSocket?.State != WebSocketState.Open)
            return;

        var payload = "42[\"claw.targets.refresh\",{}]";
        var bytes = Encoding.UTF8.GetBytes(payload);
        await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    public override async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _running = false;

        if (_webSocket != null)
        {
            try
            {
                if (_webSocket.State == WebSocketState.Open)
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Stopping", cancellationToken);
                }
            }
            catch { }
            _webSocket.Dispose();
            _webSocket = null;
        }

        _httpClient.Dispose();
        _logger.LogInformation("Mochat channel stopped");
    }

    public override async Task SendMessageAsync(OutboundMessage message, CancellationToken cancellationToken = default)
    {
        var url = $"{_config.BaseUrl}/api/claw/sessions/send";

        var payload = new
        {
            session_id = message.ChatId,
            content = message.Content,
            agent_user_id = _config.AgentUserId
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Authorization", $"Bearer {_config.ClawToken}");
        request.Content = JsonContent.Create(payload);

        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending Mochat message");
        }
    }
}
