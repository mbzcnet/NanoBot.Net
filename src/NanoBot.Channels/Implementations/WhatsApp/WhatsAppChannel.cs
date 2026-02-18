using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NanoBot.Channels.Abstractions;
using NanoBot.Core.Bus;
using NanoBot.Core.Configuration;

namespace NanoBot.Channels.Implementations.WhatsApp;

public class WhatsAppChannel : ChannelBase
{
    public override string Id => "whatsapp";
    public override string Type => "whatsapp";

    private readonly WhatsAppConfig _config;
    private ClientWebSocket? _webSocket;

    public event EventHandler<WhatsAppStatusEventArgs>? StatusChanged;
    public event EventHandler<string>? QrCodeReceived;

    public WhatsAppChannel(WhatsAppConfig config, IMessageBus bus, ILogger<WhatsAppChannel> logger)
        : base(bus, logger)
    {
        _config = config;
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_config.BridgeUrl))
        {
            _logger.LogError("WhatsApp bridge URL not configured");
            return;
        }

        _running = true;

        while (_running && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Connecting to WhatsApp bridge...");
                await ConnectAndRunAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WhatsApp bridge error");
                if (_running)
                {
                    _logger.LogInformation("Reconnecting to WhatsApp bridge in 5 seconds...");
                    await Task.Delay(5000, cancellationToken);
                }
            }
        }
    }

    private async Task ConnectAndRunAsync(CancellationToken cancellationToken)
    {
        _webSocket = new ClientWebSocket();
        await _webSocket.ConnectAsync(new Uri(_config.BridgeUrl), cancellationToken);

        if (!string.IsNullOrEmpty(_config.BridgeToken))
        {
            var auth = JsonSerializer.Serialize(new { type = "auth", token = _config.BridgeToken });
            var bytes = Encoding.UTF8.GetBytes(auth);
            await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
        }

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

            await HandleBridgeMessageAsync(message, cancellationToken);
        }
    }

    private async Task HandleBridgeMessageAsync(JsonElement message, CancellationToken cancellationToken)
    {
        var type = message.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;

        switch (type)
        {
            case "status":
                var status = message.TryGetProperty("status", out var statusEl) ? statusEl.GetString() : null;
                var connected = status == "connected";
                StatusChanged?.Invoke(this, new WhatsAppStatusEventArgs { Connected = connected });
                _logger.LogInformation("WhatsApp status: {Status}", status);
                break;

            case "qr":
                var qr = message.TryGetProperty("qr", out var qrEl) ? qrEl.GetString() : null;
                if (!string.IsNullOrEmpty(qr))
                {
                    QrCodeReceived?.Invoke(this, qr);
                    _logger.LogInformation("WhatsApp QR code received");
                }
                break;

            case "message":
                await HandleIncomingMessageAsync(message, cancellationToken);
                break;
        }
    }

    private async Task HandleIncomingMessageAsync(JsonElement message, CancellationToken cancellationToken)
    {
        var data = message.TryGetProperty("data", out var dataEl) ? dataEl : default;

        var senderId = data.TryGetProperty("from", out var fromEl) ? fromEl.GetString() : null;
        var chatId = senderId;
        var content = data.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() ?? "" : "";

        if (string.IsNullOrEmpty(senderId))
            return;

        if (!IsAllowed(senderId, _config.AllowFrom))
            return;

        await HandleMessageAsync(
            senderId,
            chatId ?? senderId,
            content,
            Array.Empty<string>(),
            new Dictionary<string, object>
            {
                ["message_id"] = data.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : ""
            }
        );
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

        _logger.LogInformation("WhatsApp channel stopped");
    }

    public override async Task SendMessageAsync(OutboundMessage message, CancellationToken cancellationToken = default)
    {
        if (_webSocket?.State != WebSocketState.Open)
        {
            _logger.LogWarning("WhatsApp bridge not connected");
            return;
        }

        var payload = new
        {
            type = "send",
            to = message.ChatId,
            content = message.Content
        };

        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
    }
}

public class WhatsAppStatusEventArgs : EventArgs
{
    public bool Connected { get; set; }
}
