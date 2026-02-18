using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NanoBot.Channels.Abstractions;
using NanoBot.Core.Bus;
using NanoBot.Core.Configuration;

namespace NanoBot.Channels.Implementations.QQ;

public class QQChannel : ChannelBase
{
    public override string Id => "qq";
    public override string Type => "qq";

    private readonly QQConfig _config;
    private readonly HttpClient _httpClient;
    private ClientWebSocket? _webSocket;
    private string? _accessToken;
    private readonly ConcurrentDictionary<string, byte> _processedMessageIds = new();

    private const string QQApiBase = "https://api.sgroup.qq.com";

    public QQChannel(QQConfig config, IMessageBus bus, ILogger<QQChannel> logger)
        : base(bus, logger)
    {
        _config = config;
        _httpClient = new HttpClient();
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_config.AppId) || string.IsNullOrEmpty(_config.Secret))
        {
            _logger.LogError("QQ app_id and secret not configured");
            return;
        }

        _running = true;

        try
        {
            _accessToken = await GetAccessTokenAsync(cancellationToken);
            if (string.IsNullOrEmpty(_accessToken))
            {
                _logger.LogError("Failed to get QQ access token");
                return;
            }

            _logger.LogInformation("QQ channel started");

            while (_running && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start QQ channel");
        }
    }

    private async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var url = $"https://bots.qq.com/app/getAppAccessToken";
        var payload = new
        {
            appId = _config.AppId,
            clientSecret = _config.Secret
        };

        var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        if (result.TryGetProperty("access_token", out var token))
        {
            return token.GetString();
        }

        return null;
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
        _logger.LogInformation("QQ channel stopped");
    }

    public override async Task SendMessageAsync(OutboundMessage message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_accessToken))
        {
            _logger.LogWarning("QQ access token not available");
            return;
        }

        var url = $"{QQApiBase}/v2/users/{message.ChatId}/messages";

        var payload = new
        {
            content = message.Content,
            msg_type = 0
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Authorization", $"QQBot {_accessToken}");
        request.Content = JsonContent.Create(payload);

        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending QQ message");
        }
    }

    public async Task SendPrivateMessageAsync(string openId, string content, CancellationToken cancellationToken = default)
    {
        await SendMessageAsync(new OutboundMessage
        {
            Channel = Type,
            ChatId = openId,
            Content = content
        }, cancellationToken);
    }

    private async Task HandleIncomingMessageAsync(JsonElement data, CancellationToken cancellationToken)
    {
        var messageId = data.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        if (string.IsNullOrEmpty(messageId) || _processedMessageIds.ContainsKey(messageId))
            return;

        _processedMessageIds[messageId] = 0;
        if (_processedMessageIds.Count > 1000)
        {
            var keysToRemove = _processedMessageIds.Keys.Take(500);
            foreach (var key in keysToRemove)
            {
                _processedMessageIds.TryRemove(key, out _);
            }
        }

        var author = data.TryGetProperty("author", out var authorEl) ? authorEl : default;
        var senderId = author.TryGetProperty("id", out var authorIdEl) ? authorIdEl.GetString() : null;
        var content = data.TryGetProperty("content", out var contentEl) ? contentEl.GetString() ?? "" : "";

        if (string.IsNullOrEmpty(senderId))
            return;

        if (!IsAllowed(senderId, _config.AllowFrom))
            return;

        await HandleMessageAsync(
            senderId,
            senderId,
            content,
            Array.Empty<string>(),
            new Dictionary<string, object>
            {
                ["message_id"] = messageId
            }
        );
    }
}
