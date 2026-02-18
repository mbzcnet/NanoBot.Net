using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NanoBot.Channels.Abstractions;
using NanoBot.Core.Bus;
using NanoBot.Core.Configuration;

namespace NanoBot.Channels.Implementations.DingTalk;

public class DingTalkChannel : ChannelBase
{
    public override string Id => "dingtalk";
    public override string Type => "dingtalk";

    private readonly DingTalkConfig _config;
    private readonly HttpClient _httpClient;
    private ClientWebSocket? _webSocket;
    private string? _accessToken;
    private DateTime _tokenExpiresAt;

    private const string DingTalkApiBase = "https://api.dingtalk.com";

    public DingTalkChannel(DingTalkConfig config, IMessageBus bus, ILogger<DingTalkChannel> logger)
        : base(bus, logger)
    {
        _config = config;
        _httpClient = new HttpClient();
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_config.ClientId) || string.IsNullOrEmpty(_config.ClientSecret))
        {
            _logger.LogError("DingTalk client_id and client_secret not configured");
            return;
        }

        _running = true;

        try
        {
            _accessToken = await GetAccessTokenAsync(cancellationToken);
            if (string.IsNullOrEmpty(_accessToken))
            {
                _logger.LogError("Failed to get DingTalk access token");
                return;
            }

            _logger.LogInformation("DingTalk channel started");

            while (_running && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start DingTalk channel");
        }
    }

    private async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var url = $"https://oapi.dingtalk.com/gettoken?appkey={_config.ClientId}&appsecret={_config.ClientSecret}";

        var response = await _httpClient.GetAsync(url, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        if (result.TryGetProperty("access_token", out var token))
        {
            _tokenExpiresAt = DateTime.UtcNow.AddHours(2);
            return token.GetString();
        }

        return null;
    }

    private async Task EnsureTokenAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_accessToken) || DateTime.UtcNow >= _tokenExpiresAt)
        {
            _accessToken = await GetAccessTokenAsync(cancellationToken);
        }
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
        _logger.LogInformation("DingTalk channel stopped");
    }

    public override async Task SendMessageAsync(OutboundMessage message, CancellationToken cancellationToken = default)
    {
        await EnsureTokenAsync(cancellationToken);

        if (string.IsNullOrEmpty(_accessToken))
        {
            _logger.LogWarning("DingTalk access token not available");
            return;
        }

        var url = $"{DingTalkApiBase}/v1.0/robot/oToMessages/batchSend";

        var payload = new
        {
            robotCode = _config.ClientId,
            userIds = new[] { message.ChatId },
            msgKey = "sampleText",
            msgParam = JsonSerializer.Serialize(new { content = message.Content })
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("x-acs-dingtalk-access-token", _accessToken);
        request.Content = JsonContent.Create(payload);

        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending DingTalk message");
        }
    }

    public async Task SendPrivateMessageAsync(string userId, string content, CancellationToken cancellationToken = default)
    {
        await SendMessageAsync(new OutboundMessage
        {
            Channel = Type,
            ChatId = userId,
            Content = content
        }, cancellationToken);
    }
}
