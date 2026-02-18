using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NanoBot.Channels.Abstractions;
using NanoBot.Core.Bus;
using NanoBot.Core.Configuration;

namespace NanoBot.Channels.Implementations.Slack;

public partial class SlackChannel : ChannelBase
{
    public override string Id => "slack";
    public override string Type => "slack";

    private readonly SlackConfig _config;
    private readonly HttpClient _httpClient;
    private ClientWebSocket? _webSocket;
    private string? _botUserId;

    private const string SlackApiBase = "https://slack.com/api";

    public SlackChannel(SlackConfig config, IMessageBus bus, ILogger<SlackChannel> logger)
        : base(bus, logger)
    {
        _config = config;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_config.BotToken}");
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_config.BotToken) || string.IsNullOrEmpty(_config.AppToken))
        {
            _logger.LogError("Slack bot/app token not configured");
            return;
        }

        if (_config.Mode != "socket")
        {
            _logger.LogError("Unsupported Slack mode: {Mode}", _config.Mode);
            return;
        }

        _running = true;

        try
        {
            var authResult = await SlackApiGetAsync("auth.test", cancellationToken);
            if (authResult != null && authResult.Value.TryGetProperty("user_id", out var userId))
            {
                _botUserId = userId.GetString();
                _logger.LogInformation("Slack bot connected as {BotUserId}", _botUserId);
            }

            var connectResult = await SlackApiPostAsync("apps.connections.open", new { app_token = _config.AppToken }, cancellationToken);
            if (connectResult == null || !connectResult.Value.TryGetProperty("url", out var wsUrl))
            {
                _logger.LogError("Failed to get Slack WebSocket URL");
                return;
            }

            _webSocket = new ClientWebSocket();
            await _webSocket.ConnectAsync(new Uri(wsUrl.GetString()!), cancellationToken);

            _logger.LogInformation("Starting Slack Socket Mode client...");

            _ = ReceiveMessagesAsync(cancellationToken);

            while (_running && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Slack channel");
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

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var message = JsonSerializer.Deserialize<JsonElement>(json);

                if (message.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "events_api")
                {
                    var envelopeId = message.GetProperty("envelope_id").GetString()!;
                    await SendAckAsync(envelopeId, cancellationToken);

                    if (message.TryGetProperty("payload", out var payload) &&
                        payload.TryGetProperty("event", out var evt))
                    {
                        await HandleEventAsync(evt, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error receiving Slack message");
            }
        }
    }

    private async Task SendAckAsync(string envelopeId, CancellationToken cancellationToken)
    {
        if (_webSocket?.State != WebSocketState.Open)
            return;

        var ack = JsonSerializer.Serialize(new { envelope_id = envelopeId });
        var bytes = Encoding.UTF8.GetBytes(ack);
        await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
    }

    private async Task HandleEventAsync(JsonElement evt, CancellationToken cancellationToken)
    {
        var eventType = evt.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;

        if (eventType != "message" && eventType != "app_mention")
            return;

        if (evt.TryGetProperty("subtype", out _))
            return;

        var senderId = evt.TryGetProperty("user", out var userEl) ? userEl.GetString() : null;
        var chatId = evt.TryGetProperty("channel", out var channelEl) ? channelEl.GetString() : null;
        var text = evt.TryGetProperty("text", out var textEl) ? textEl.GetString() ?? "" : "";

        if (string.IsNullOrEmpty(senderId) || string.IsNullOrEmpty(chatId))
            return;

        if (senderId == _botUserId)
            return;

        if (eventType == "message" && _botUserId != null && text.Contains($"<@{_botUserId}>"))
            return;

        var channelType = evt.TryGetProperty("channel_type", out var ctEl) ? ctEl.GetString() ?? "" : "";

        if (!IsAllowed(senderId, chatId, channelType))
            return;

        if (channelType != "im" && !ShouldRespondInChannel(eventType, text, chatId))
            return;

        text = StripBotMention(text);

        var threadTs = evt.TryGetProperty("thread_ts", out var ttsEl)
            ? ttsEl.GetString()
            : evt.TryGetProperty("ts", out var tsEl) ? tsEl.GetString() : null;

        if (!string.IsNullOrEmpty(evt.TryGetProperty("ts", out var ts) ? ts.GetString() : null))
        {
            try
            {
                await _httpClient.PostAsync($"{SlackApiBase}/reactions.add",
                    new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["channel"] = chatId,
                        ["name"] = "eyes",
                        ["timestamp"] = evt.GetProperty("ts").GetString()!
                    }), cancellationToken);
            }
            catch { }
        }

        await HandleMessageAsync(
            senderId,
            chatId,
            text,
            Array.Empty<string>(),
            new Dictionary<string, object>
            {
                ["slack"] = new Dictionary<string, object?>
                {
                    ["thread_ts"] = threadTs,
                    ["channel_type"] = channelType
                }
            }
        );
    }

    private bool IsAllowed(string senderId, string chatId, string channelType)
    {
        if (channelType == "im")
        {
            if (!_config.Dm.Enabled)
                return false;

            if (_config.Dm.Policy == "allowlist")
                return _config.Dm.AllowFrom.Contains(senderId);

            return true;
        }

        if (_config.GroupPolicy == "allowlist")
            return _config.GroupAllowFrom.Contains(chatId);

        return true;
    }

    private bool ShouldRespondInChannel(string eventType, string text, string chatId)
    {
        if (_config.GroupPolicy == "open")
            return true;

        if (_config.GroupPolicy == "mention")
        {
            if (eventType == "app_mention")
                return true;

            return _botUserId != null && text.Contains($"<@{_botUserId}>");
        }

        if (_config.GroupPolicy == "allowlist")
            return _config.GroupAllowFrom.Contains(chatId);

        return false;
    }

    private string StripBotMention(string text)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(_botUserId))
            return text;

        return BotMentionRegex().Replace(text, "").Trim();
    }

    [GeneratedRegex(@"<@[A-Z0-9]+>\s*")]
    private static partial Regex BotMentionRegex();

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
        _logger.LogInformation("Slack channel stopped");
    }

    public override async Task SendMessageAsync(OutboundMessage message, CancellationToken cancellationToken = default)
    {
        var slackMeta = message.Metadata?.TryGetValue("slack", out var meta) == true
            ? meta as Dictionary<string, object?>
            : null;

        var threadTs = slackMeta?.GetValueOrDefault("thread_ts") as string;
        var channelType = slackMeta?.GetValueOrDefault("channel_type") as string;

        var useThread = !string.IsNullOrEmpty(threadTs) && channelType != "im";

        var payload = new Dictionary<string, object?>
        {
            ["channel"] = message.ChatId,
            ["text"] = ToMrkdwn(message.Content)
        };

        if (useThread)
        {
            payload["thread_ts"] = threadTs;
        }

        try
        {
            await SlackApiPostAsync("chat.postMessage", payload, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending Slack message");
        }
    }

    private async Task<JsonElement?> SlackApiGetAsync(string method, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"{SlackApiBase}/{method}", cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<JsonElement>(content);
    }

    private async Task<JsonElement?> SlackApiPostAsync(string method, object payload, CancellationToken cancellationToken)
    {
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"{SlackApiBase}/{method}", content, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<JsonElement>(responseContent);
    }

    private static string ToMrkdwn(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        text = TableRegex().Replace(text, ConvertTable);

        return text;
    }

    [GeneratedRegex(@"(?m)^\|.*\|$(?:\n\|[\s:|-]*\|$)(?:\n\|.*\|$)*")]
    private static partial Regex TableRegex();

    private static string ConvertTable(Match match)
    {
        var lines = match.Value.Trim().Split('\n').Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l)).ToList();

        if (lines.Count < 2)
            return match.Value;

        var headers = lines[0].Trim('|').Split('|').Select(h => h.Trim()).ToList();
        var start = System.Text.RegularExpressions.Regex.IsMatch(lines[1], @"^[|\s:\-]+$") ? 2 : 1;

        var rows = new List<string>();
        for (var i = start; i < lines.Count; i++)
        {
            var cells = lines[i].Trim('|').Split('|').Select(c => c.Trim()).ToList();
            cells = cells.Concat(Enumerable.Repeat("", headers.Count - cells.Count)).Take(headers.Count).ToList();

            var parts = new List<string>();
            for (var j = 0; j < headers.Count; j++)
            {
                if (!string.IsNullOrEmpty(cells[j]))
                    parts.Add($"**{headers[j]}**: {cells[j]}");
            }

            if (parts.Count > 0)
                rows.Add(string.Join(" · ", parts));
        }

        return string.Join("\n", rows);
    }
}
