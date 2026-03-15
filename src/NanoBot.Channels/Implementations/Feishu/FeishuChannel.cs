using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NanoBot.Channels.Abstractions;
using NanoBot.Core.Bus;
using NanoBot.Core.Configuration;

namespace NanoBot.Channels.Implementations.Feishu;

public class FeishuChannel : ChannelBase
{
    public override string Id => "feishu";
    public override string Type => "feishu";

    private readonly FeishuConfig _config;
    private readonly HttpClient _httpClient;
    private ClientWebSocket? _webSocket;
    private string? _accessToken;
    private readonly ConcurrentDictionary<string, byte> _processedMessageIds = new();
    private const int MaxCacheSize = 1000;

    private static readonly Dictionary<string, string> MsgTypeMap = new()
    {
        ["image"] = "[image]",
        ["audio"] = "[audio]",
        ["file"] = "[file]",
        ["sticker"] = "[sticker]"
    };

    private static readonly HashSet<string> ImageExtensions = new() { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico", ".tiff", ".tif" };
    private static readonly HashSet<string> AudioExtensions = new() { ".opus" };
    private readonly string _mediaDirectory;

    public override IDictionary<string, object?>? DefaultConfig()
    {
        return new Dictionary<string, object?>
        {
            ["enabled"] = false,
            ["appId"] = "",
            ["appSecret"] = "",
            ["encryptKey"] = "",
            ["verificationToken"] = "",
            ["allowFrom"] = Array.Empty<string>()
        };
    }

    public FeishuChannel(FeishuConfig config, IMessageBus bus, ILogger<FeishuChannel> logger)
        : base(bus, logger)
    {
        _config = config;
        _httpClient = new HttpClient();
        _mediaDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nanobot", "media");
        
        if (!Directory.Exists(_mediaDirectory))
        {
            Directory.CreateDirectory(_mediaDirectory);
        }
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_config.AppId) || string.IsNullOrEmpty(_config.AppSecret))
        {
            _logger.LogError("Feishu app_id and app_secret not configured");
            return;
        }

        _running = true;

        try
        {
            _accessToken = await GetAccessTokenAsync(cancellationToken);
            if (string.IsNullOrEmpty(_accessToken))
            {
                _logger.LogError("Failed to get Feishu access token");
                return;
            }

            _logger.LogInformation("Feishu bot started with WebSocket long connection");

            while (_running && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Feishu channel");
        }
    }

    private async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var url = "https://open.feishu.cn/open-apis/auth/v3/tenant_access_token/internal";
        var payload = new
        {
            app_id = _config.AppId,
            app_secret = _config.AppSecret
        };

        var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        if (result.TryGetProperty("tenant_access_token", out var token))
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
        _logger.LogInformation("Feishu channel stopped");
    }

    public override async Task SendMessageAsync(OutboundMessage message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_accessToken))
        {
            _logger.LogWarning("Feishu access token not available");
            return;
        }

        var receiveIdType = message.ChatId.StartsWith("oc_") ? "chat_id" : "open_id";

        var card = new
        {
            config = new { wide_screen_mode = true },
            elements = BuildCardElements(message.Content)
        };

        var payload = new
        {
            receive_id_type = receiveIdType,
            msg_type = "interactive",
            content = JsonSerializer.Serialize(card),
            receive_id = message.ChatId
        };

        var url = "https://open.feishu.cn/open-apis/im/v1/messages";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Authorization", $"Bearer {_accessToken}");
        request.Content = JsonContent.Create(payload);

        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken);
            var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

            if (result.TryGetProperty("code", out var code) && code.GetInt32() != 0)
            {
                _logger.LogError("Failed to send Feishu message: code={Code}", code);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending Feishu message");
        }
    }

    private List<Dictionary<string, object?>> BuildCardElements(string content)
    {
        var elements = new List<Dictionary<string, object?>>();

        // Simple markdown element for now
        elements.Add(new Dictionary<string, object?>
        {
            ["tag"] = "markdown",
            ["content"] = content
        });

        return elements;
    }

    private async Task HandleIncomingMessageAsync(JsonElement eventData, CancellationToken cancellationToken)
    {
        var message = eventData.GetProperty("message");
        var sender = eventData.GetProperty("sender");

        var messageId = message.GetProperty("message_id").GetString() ?? "";
        if (string.IsNullOrEmpty(messageId) || _processedMessageIds.ContainsKey(messageId))
            return;

        _processedMessageIds[messageId] = 0;
        if (_processedMessageIds.Count > MaxCacheSize)
        {
            var keysToRemove = _processedMessageIds.Keys.Take(_processedMessageIds.Count - MaxCacheSize / 2);
            foreach (var key in keysToRemove)
            {
                _processedMessageIds.TryRemove(key, out _);
            }
        }

        var senderType = sender.GetProperty("sender_type").GetString();
        if (senderType == "bot")
            return;

        var senderId = sender.GetProperty("sender_id").GetProperty("open_id").GetString() ?? "unknown";
        var chatId = message.GetProperty("chat_id").GetString() ?? "";
        var chatType = message.GetProperty("chat_type").GetString() ?? "";
        var msgType = message.GetProperty("msg_type").GetString() ?? "";

        string content;
        var mediaPaths = new List<string>();

        if (msgType == "text")
        {
            content = ExtractTextContent(message);
        }
        else if (msgType == "post")
        {
            var (textContent, imageKeys) = ExtractPostContentWithImages(message);
            content = textContent;

            // Download images from post message
            foreach (var imageKey in imageKeys)
            {
                var (filePath, contentText) = await DownloadImageByKeyAsync(messageId, imageKey, cancellationToken);
                if (filePath != null)
                {
                    mediaPaths.Add(filePath);
                }
            }
        }
        else if (msgType == "image" || msgType == "audio" || msgType == "file" || msgType == "media")
        {
            var (filePath, contentText) = await DownloadAndSaveMediaAsync(msgType, message, messageId, cancellationToken);
            if (filePath != null)
            {
                mediaPaths.Add(filePath);
            }
            content = contentText;
        }
        else
        {
            content = MsgTypeMap.GetValueOrDefault(msgType, $"[{msgType}]");
        }

        if (string.IsNullOrEmpty(content) && mediaPaths.Count == 0)
            return;

        var replyTo = chatType == "group" ? chatId : senderId;

        await HandleMessageAsync(
            senderId,
            replyTo,
            content,
            mediaPaths,
            new Dictionary<string, object>
            {
                ["message_id"] = messageId,
                ["chat_type"] = chatType,
                ["msg_type"] = msgType
            }
        );
    }

    private static string ExtractTextContent(JsonElement message)
    {
        var content = message.GetProperty("content").GetString() ?? "{}";
        var json = JsonSerializer.Deserialize<JsonElement>(content);
        return json.TryGetProperty("text", out var text) ? text.GetString() ?? "" : "";
    }

    private static (string Text, List<string> ImageKeys) ExtractPostContentWithImages(JsonElement message)
    {
        var content = message.GetProperty("content").GetString() ?? "{}";
        var json = JsonSerializer.Deserialize<JsonElement>(content);

        var textParts = new List<string>();
        var imageKeys = new List<string>();

        // Handle different payload shapes
        var root = json;

        // Unwrap optional {"post": ...} envelope
        if (json.TryGetProperty("post", out var postWrapper))
        {
            root = postWrapper;
        }

        // Try known locales first
        foreach (var locale in new[] { "zh_cn", "en_us", "ja_jp" })
        {
            if (root.TryGetProperty(locale, out var localeContent))
            {
                ExtractFromLocale(localeContent, textParts, imageKeys);
                if (textParts.Count > 0 || imageKeys.Count > 0)
                {
                    return (string.Join(" ", textParts), imageKeys);
                }
            }
        }

        // Fall back to any dict child
        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Object)
            {
                ExtractFromLocale(prop.Value, textParts, imageKeys);
                if (textParts.Count > 0 || imageKeys.Count > 0)
                {
                    return (string.Join(" ", textParts), imageKeys);
                }
            }
        }

        return (string.Empty, imageKeys);
    }

    private static void ExtractFromLocale(JsonElement langContent, List<string> textParts, List<string> imageKeys)
    {
        // Handle title
        if (langContent.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
        {
            textParts.Add(title.GetString() ?? "");
        }

        // Handle content array
        if (langContent.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var element in block.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.Object)
                        continue;

                    var tag = element.TryGetProperty("tag", out var tagEl) ? tagEl.GetString() : null;

                    if (tag == "text" || tag == "a")
                    {
                        if (element.TryGetProperty("text", out var text))
                            textParts.Add(text.GetString() ?? "");
                    }
                    else if (tag == "at")
                    {
                        var userName = element.TryGetProperty("user_name", out var un) ? un.GetString() : "user";
                        textParts.Add($"@{userName}");
                    }
                    else if (tag == "img")
                    {
                        if (element.TryGetProperty("image_key", out var imgKey))
                        {
                            var imageKey = imgKey.GetString();
                            if (!string.IsNullOrEmpty(imageKey))
                            {
                                imageKeys.Add(imageKey);
                            }
                        }
                    }
                }
            }
        }
    }

    private static string ExtractPostContent(JsonElement message)
    {
        var (text, _) = ExtractPostContentWithImages(message);
        return text;
    }

    private async Task<(string? FilePath, string ContentText)> DownloadAndSaveMediaAsync(
        string msgType,
        JsonElement message,
        string messageId,
        CancellationToken cancellationToken)
    {
        try
        {
            var contentStr = message.GetProperty("content").GetString() ?? "{}";
            var contentJson = JsonSerializer.Deserialize<JsonElement>(contentStr);

            string? fileKey = null;
            string resourceType = msgType;

            if (msgType == "image")
            {
                fileKey = contentJson.TryGetProperty("image_key", out var imgKey) ? imgKey.GetString() : null;
            }
            else if (msgType == "audio" || msgType == "file" || msgType == "media")
            {
                fileKey = contentJson.TryGetProperty("file_key", out var fKey) ? fKey.GetString() : null;
            }

            if (string.IsNullOrEmpty(fileKey))
            {
                return (null, MsgTypeMap.GetValueOrDefault(msgType, $"[{msgType}]"));
            }

            var (fileData, fileName) = await DownloadFileAsync(messageId, fileKey, resourceType, cancellationToken);

            if (fileData == null)
            {
                return (null, MsgTypeMap.GetValueOrDefault(msgType, $"[{msgType}]"));
            }

            if (string.IsNullOrEmpty(fileName))
            {
                var ext = msgType switch
                {
                    "image" => ".jpg",
                    "audio" => ".opus",
                    "media" => ".mp4",
                    _ => ""
                };
                fileName = $"{fileKey[..Math.Min(16, fileKey.Length)]}{ext}";
            }

            var filePath = Path.Combine(_mediaDirectory, fileName);
            await File.WriteAllBytesAsync(filePath, fileData, cancellationToken);

            _logger.LogDebug("Downloaded {MsgType} to {FilePath}", msgType, filePath);

            return (filePath, MsgTypeMap.GetValueOrDefault(msgType, $"[{msgType}]"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading {MsgType}", msgType);
            return (null, MsgTypeMap.GetValueOrDefault(msgType, $"[{msgType}]"));
        }
    }

    private async Task<(string? FilePath, string? FileName)> DownloadImageByKeyAsync(
        string messageId,
        string imageKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_accessToken))
        {
            _logger.LogWarning("Feishu access token not available for image download");
            return (null, null);
        }

        try
        {
            var url = $"https://open.feishu.cn/open-apis/im/v1/messages/{messageId}/resources/{imageKey}?type=image";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", $"Bearer {_accessToken}");

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to download image: status={StatusCode}", response.StatusCode);
                return (null, null);
            }

            var fileData = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? $"{imageKey[..Math.Min(16, imageKey.Length)]}.jpg";

            var filePath = Path.Combine(_mediaDirectory, fileName);
            await File.WriteAllBytesAsync(filePath, fileData, cancellationToken);

            _logger.LogDebug("Downloaded image {ImageKey} to {FilePath}", imageKey, filePath);

            return (filePath, $"[image: {fileName}]");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading image {ImageKey}", imageKey);
            return (null, null);
        }
    }

    private async Task<(byte[]? FileData, string? FileName)> DownloadFileAsync(
        string messageId,
        string fileKey,
        string resourceType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_accessToken))
        {
            _logger.LogWarning("Feishu access token not available for file download");
            return (null, null);
        }

        try
        {
            var url = $"https://open.feishu.cn/open-apis/im/v1/messages/{messageId}/resources/{fileKey}?type={resourceType}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", $"Bearer {_accessToken}");

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to download {ResourceType}: status={StatusCode}", resourceType, response.StatusCode);
                return (null, null);
            }

            var fileData = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"');

            return (fileData, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading {ResourceType} {FileKey}", resourceType, fileKey);
            return (null, null);
        }
    }
}
