using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NanoBot.Channels.Abstractions;
using NanoBot.Core.Bus;
using NanoBot.Core.Configuration;

namespace NanoBot.Channels.Implementations.Discord;

public class DiscordChannel : ChannelBase
{
    public override string Id => "discord";
    public override string Type => "discord";

    private readonly DiscordConfig _config;
    private readonly HttpClient _httpClient;
    private ClientWebSocket? _webSocket;
    private int? _sequence;
    private CancellationTokenSource? _heartbeatCts;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _typingTasks = new();

    private const string DiscordApiBase = "https://discord.com/api/v10";
    private const int MaxAttachmentBytes = 20 * 1024 * 1024;

    public DiscordChannel(DiscordConfig config, IMessageBus bus, ILogger<DiscordChannel> logger)
        : base(bus, logger)
    {
        _config = config;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bot {_config.Token}");
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_config.Token))
        {
            _logger.LogError("Discord bot token not configured");
            return;
        }

        _running = true;

        while (_running && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Connecting to Discord gateway...");
                await ConnectAndRunAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Discord gateway error");
                if (_running)
                {
                    _logger.LogInformation("Reconnecting to Discord gateway in 5 seconds...");
                    await Task.Delay(5000, cancellationToken);
                }
            }
        }
    }

    private async Task ConnectAndRunAsync(CancellationToken cancellationToken)
    {
        _webSocket = new ClientWebSocket();
        await _webSocket.ConnectAsync(new Uri(_config.GatewayUrl), cancellationToken);

        var buffer = new byte[8192];

        while (_webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", cancellationToken);
                break;
            }

            var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            var message = JsonSerializer.Deserialize<JsonElement>(json);

            var op = message.GetProperty("op").GetInt32();
            if (message.TryGetProperty("s", out var seqEl) && seqEl.ValueKind != JsonValueKind.Null)
            {
                _sequence = seqEl.GetInt32();
            }

            switch (op)
            {
                case 10:
                    var data = message.GetProperty("d");
                    var heartbeatInterval = data.GetProperty("heartbeat_interval").GetInt32();
                    _ = StartHeartbeatAsync(heartbeatInterval, cancellationToken);
                    await IdentifyAsync(cancellationToken);
                    break;

                case 0:
                    var eventType = message.GetProperty("t").GetString();
                    if (eventType == "READY")
                    {
                        _logger.LogInformation("Discord gateway READY");
                    }
                    else if (eventType == "MESSAGE_CREATE")
                    {
                        await HandleMessageCreateAsync(message.GetProperty("d"), cancellationToken);
                    }
                    break;

                case 7:
                    _logger.LogInformation("Discord gateway requested reconnect");
                    return;

                case 9:
                    _logger.LogWarning("Discord gateway invalid session");
                    return;
            }
        }
    }

    private async Task IdentifyAsync(CancellationToken cancellationToken)
    {
        var identify = new
        {
            op = 2,
            d = new
            {
                token = _config.Token,
                intents = _config.Intents,
                properties = new
                {
                    os = "nanobot",
                    browser = "nanobot",
                    device = "nanobot"
                }
            }
        };

        await SendAsync(identify, cancellationToken);
    }

    private async Task StartHeartbeatAsync(int intervalMs, CancellationToken cancellationToken)
    {
        _heartbeatCts?.Cancel();
        _heartbeatCts = new CancellationTokenSource();

        _ = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested && _heartbeatCts is { IsCancellationRequested: false })
            {
                try
                {
                    var heartbeat = new { op = 1, d = _sequence };
                    await SendAsync(heartbeat, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Discord heartbeat failed");
                }

                await Task.Delay(intervalMs, cancellationToken);
            }
        }, cancellationToken);
    }

    private async Task HandleMessageCreateAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var author = payload.GetProperty("author");
        if (author.TryGetProperty("bot", out var botProp) && botProp.GetBoolean())
            return;

        var senderId = author.GetProperty("id").GetString() ?? "";
        var channelId = payload.GetProperty("channel_id").GetString() ?? "";
        var content = payload.TryGetProperty("content", out var contentEl) ? contentEl.GetString() ?? "" : "";

        if (string.IsNullOrEmpty(senderId) || string.IsNullOrEmpty(channelId))
            return;

        if (!IsAllowed(senderId, _config.AllowFrom))
            return;

        var contentParts = new List<string>();
        if (!string.IsNullOrEmpty(content))
            contentParts.Add(content);

        var mediaPaths = new List<string>();

        if (payload.TryGetProperty("attachments", out var attachments) && attachments.ValueKind == JsonValueKind.Array)
        {
            foreach (var attachment in attachments.EnumerateArray())
            {
                var url = attachment.TryGetProperty("url", out var urlEl) ? urlEl.GetString() : null;
                var filename = attachment.TryGetProperty("filename", out var fnEl) ? fnEl.GetString() : "attachment";
                var size = attachment.TryGetProperty("size", out var sizeEl) ? sizeEl.GetInt32() : 0;

                if (string.IsNullOrEmpty(url))
                    continue;

                if (size > MaxAttachmentBytes)
                {
                    contentParts.Add($"[attachment: {filename} - too large]");
                    continue;
                }

                contentParts.Add($"[attachment: {filename}]");
            }
        }

        string? replyTo = null;
        if (payload.TryGetProperty("referenced_message", out var refMsg) && refMsg.ValueKind != JsonValueKind.Null)
        {
            replyTo = refMsg.TryGetProperty("id", out var refId) ? refId.GetString() : null;
        }

        _ = StartTypingAsync(channelId, cancellationToken);

        await HandleMessageAsync(
            senderId,
            channelId,
            string.Join("\n", contentParts) ?? "[empty message]",
            mediaPaths,
            new Dictionary<string, object>
            {
                ["message_id"] = payload.GetProperty("id").GetString() ?? "",
                ["guild_id"] = payload.TryGetProperty("guild_id", out var guildId) ? guildId.GetString() : null,
                ["reply_to"] = replyTo
            }
        );
    }

    public override async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _running = false;

        _heartbeatCts?.Cancel();
        _heartbeatCts?.Dispose();

        foreach (var cts in _typingTasks.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _typingTasks.Clear();

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
        _logger.LogInformation("Discord channel stopped");
    }

    public override async Task SendMessageAsync(OutboundMessage message, CancellationToken cancellationToken = default)
    {
        StopTyping(message.ChatId);

        var url = $"{DiscordApiBase}/channels/{message.ChatId}/messages";
        var payload = new Dictionary<string, object>
        {
            ["content"] = message.Content
        };

        if (!string.IsNullOrEmpty(message.ReplyTo))
        {
            payload["message_reference"] = new { message_id = message.ReplyTo };
            payload["allowed_mentions"] = new { replied_user = false };
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    var error = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
                    var retryAfter = error.GetProperty("retry_after").GetDouble();
                    _logger.LogWarning("Discord rate limited, retrying in {RetryAfter}s", retryAfter);
                    await Task.Delay((int)(retryAfter * 1000), cancellationToken);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                return;
            }
            catch (Exception ex)
            {
                if (attempt == 2)
                {
                    _logger.LogError(ex, "Error sending Discord message");
                }
                else
                {
                    await Task.Delay(1000, cancellationToken);
                }
            }
        }
    }

    private async Task SendAsync(object payload, CancellationToken cancellationToken)
    {
        if (_webSocket == null || _webSocket.State != WebSocketState.Open)
            return;

        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
    }

    private async Task StartTypingAsync(string channelId, CancellationToken cancellationToken)
    {
        StopTyping(channelId);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _typingTasks[channelId] = cts;

        _ = Task.Run(async () =>
        {
            var url = $"{DiscordApiBase}/channels/{channelId}/typing";
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    await _httpClient.PostAsync(url, null, cts.Token);
                }
                catch { }
                await Task.Delay(8000, cts.Token);
            }
        }, cts.Token);
    }

    private void StopTyping(string channelId)
    {
        if (_typingTasks.TryRemove(channelId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }
}
