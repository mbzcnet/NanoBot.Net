using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.Extensions.Logging;
using NanoBot.Channels.Abstractions;
using NanoBot.Core.Bus;
using NanoBot.Core.Configuration;

namespace NanoBot.Channels.Implementations.WeiXin;

/// <summary>
/// Personal WeChat channel using HTTP long-poll API (ilinkai.weixin.qq.com).
/// Authentication is via QR code login which produces a bot token.
/// </summary>
public sealed class WeiXinChannel : ChannelBase
{
    public override string Id => "weixin";
    public override string Type => "weixin";

    // Constants
    private const int ItemTypeText = 1;
    private const int ItemTypeImage = 2;
    private const int ItemTypeVoice = 3;
    private const int ItemTypeFile = 4;
    private const int ItemTypeVideo = 5;
    private const int MsgTypeUser = 1;
    private const int MsgTypeBot = 2;
    private const int MsgStateFinish = 2;
    private const int ErrSessionExpired = -14;
    private const int SessionPauseDurationS = 60 * 60;
    private const int MaxConsecutiveFailures = 3;
    private const int BackoffDelayS = 30;
    private const int RetryDelayS = 2;
    private const int MaxQrRefreshCount = 3;
    private const int DefaultLongPollTimeoutS = 35;
    private const int WeiXinMaxMsgLen = 4000;
    private const int UploadMediaTypeImage = 1;
    private const int UploadMediaTypeVideo = 2;
    private const int UploadMediaTypeFile = 3;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".ico", ".svg"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".avi", ".mov", ".mkv", ".webm", ".flv"
    };

    private readonly WeiXinConfig _config;
    private readonly HttpClient _httpClient;
    private string _token = "";
    private string _updatesBuf = "";
    private readonly ConcurrentDictionary<string, string> _contextTokens = new(); // userId -> contextToken
    private readonly OrderedDictionary<string, bool> _processedIds = new();
    private int _pollTimeoutS = DefaultLongPollTimeoutS;
    private double _sessionPauseUntil = 0.0;

    private string MediaDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".nanobot", "media", "weixin");

    public override IDictionary<string, object?>? DefaultConfig() => new Dictionary<string, object?>
    {
        ["enabled"] = false,
        ["baseUrl"] = "https://ilinkai.weixin.qq.com",
        ["cdnBaseUrl"] = "https://novac2c.cdn.weixin.qq.com/c2c",
        ["allowFrom"] = Array.Empty<string>()
    };

    public WeiXinChannel(WeiXinConfig config, IMessageBus bus, ILogger<WeiXinChannel> logger)
        : base(bus, logger)
    {
        _config = config;
        _httpClient = new HttpClient();
        Directory.CreateDirectory(MediaDirectory);
    }

    // WeiXin: empty AllowFrom means deny all (unlike the ChannelBase default)
    protected override bool IsAllowed(string senderId, IReadOnlyList<string>? allowFrom)
    {
        if (allowFrom == null || allowFrom.Count == 0)
            return false;
        if (allowFrom.Contains("*"))
            return true;
        return allowFrom.Contains(senderId, StringComparer.OrdinalIgnoreCase);
    }

    // Override PublishInboundAsync to perform IsAllowed check before publishing
    private new async Task PublishInboundAsync(
        string senderId,
        string chatId,
        string content,
        IReadOnlyList<string>? media = null,
        IDictionary<string, object>? metadata = null)
    {
        if (!IsAllowed(senderId, _config.AllowFrom))
        {
            _logger.LogWarning("WeiXin access denied for sender {SenderId}", senderId);
            return;
        }
        var message = new InboundMessage
        {
            Channel = Type,
            SenderId = senderId,
            ChatId = chatId,
            Content = content,
            Media = media ?? Array.Empty<string>(),
            ImagePaths = media ?? Array.Empty<string>(),
            Metadata = metadata,
            Timestamp = DateTimeOffset.UtcNow
        };
        OnMessageReceived(message);
        await Bus.PublishInboundAsync(message);
    }

    // ==================================================================
    // State persistence
    // ==================================================================

    private string StateDir
    {
        get
        {
            if (!string.IsNullOrEmpty(_config.StateDir))
                return _config.StateDir;
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nanobot", "weixin");
        }
    }

    private void LoadState()
    {
        var stateFile = Path.Combine(StateDir, "account.json");
        if (!File.Exists(stateFile)) return;
        try
        {
            var doc = JsonDocument.Parse(File.ReadAllText(stateFile));
            var root = doc.RootElement;
            _token = root.TryGetProperty("token", out var t) ? t.GetString() ?? "" : "";
            _updatesBuf = root.TryGetProperty("get_updates_buf", out var buf) ? buf.GetString() ?? "" : "";
            if (root.TryGetProperty("context_tokens", out var ctoks) && ctoks.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in ctoks.EnumerateObject())
                {
                    var val = prop.Value.GetString();
                    if (!string.IsNullOrEmpty(val))
                        _contextTokens[prop.Name] = val;
                }
            }
            if (root.TryGetProperty("base_url", out var bu) && bu.ValueKind == JsonValueKind.String)
                _config.BaseUrl = bu.GetString() ?? _config.BaseUrl;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to load WeiXin state: {Error}", ex.Message);
        }
    }

    private void SaveState()
    {
        try
        {
            Directory.CreateDirectory(StateDir);
            var stateFile = Path.Combine(StateDir, "account.json");
            var obj = new Dictionary<string, object>
            {
                ["token"] = _token,
                ["get_updates_buf"] = _updatesBuf,
                ["context_tokens"] = _contextTokens,
                ["base_url"] = _config.BaseUrl
            };
            File.WriteAllText(stateFile, JsonSerializer.Serialize(obj));
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to save WeiXin state: {Error}", ex.Message);
        }
    }

    // ==================================================================
    // HTTP helpers
    // ==================================================================

    private static string RandomWechatUin()
    {
        var uint32 = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4), 0);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(uint32.ToString()));
    }

    private HttpRequestMessage MakeRequest(string endpoint, HttpMethod method, JsonObject? body = null, bool auth = true)
    {
        var url = $"{_config.BaseUrl.TrimEnd('/')}/{endpoint.TrimStart('/')}";
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("X-WECHAT-UIN", RandomWechatUin());
        req.Headers.Add("AuthorizationType", "ilink_bot_token");
        if (auth && !string.IsNullOrEmpty(_token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        if (!string.IsNullOrEmpty(_config.RouteTag))
            req.Headers.Add("SKRouteTag", _config.RouteTag);
        if (body != null)
        {
            body["base_info"] = JsonNode.Parse("""{"channel_version": "1.0.0"}""")!;
            req.Content = JsonContent.Create(body);
        }
        return req;
    }

    private async Task<JsonNode?> ApiGetAsync(string endpoint, Dictionary<string, string>? query = null, bool auth = true)
    {
        var url = $"{_config.BaseUrl.TrimEnd('/')}/{endpoint.TrimStart('/')}";
        var req = MakeRequest(endpoint, HttpMethod.Get, auth: auth);
        if (query != null)
        {
            var q = HttpUtility.ParseQueryString(string.Empty);
            foreach (var kv in query) q[kv.Key] = kv.Value;
            req.RequestUri = new Uri($"{url}?{q}");
        }
        var resp = await _httpClient.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        var content = await resp.Content.ReadAsStringAsync();
        return JsonNode.Parse(content);
    }

    private async Task<JsonNode?> ApiPostAsync(string endpoint, JsonObject? body, bool auth = true)
    {
        var req = MakeRequest(endpoint, HttpMethod.Post, body, auth);
        var resp = await _httpClient.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        var content = await resp.Content.ReadAsStringAsync();
        return JsonNode.Parse(content);
    }

    // ==================================================================
    // QR Code Login
    // ==================================================================

    /// <summary>
    /// Perform QR code login. Returns true on success.
    /// </summary>
    public async Task<bool> QRLoginAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting WeiXin QR code login...");

        var refreshCount = 0;
        var (qrcodeId, scanUrl) = await FetchQrCodeAsync(cancellationToken);
        PrintQrCode(scanUrl);

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(1000, cancellationToken);

            JsonNode? statusData;
            try
            {
                var url = $"{_config.BaseUrl.TrimEnd('/')}/ilink/bot/get_qrcode_status";
                var req = new HttpRequestMessage(HttpMethod.Get, $"{url}?qrcode={Uri.EscapeDataString(qrcodeId)}");
                req.Headers.Add("X-WECHAT-UIN", RandomWechatUin());
                req.Headers.Add("iLink-App-ClientVersion", "1");
                var resp = await _httpClient.SendAsync(req, cancellationToken);
                resp.EnsureSuccessStatusCode();
                statusData = JsonNode.Parse(await resp.Content.ReadAsStringAsync(cancellationToken));
            }
            catch (HttpRequestException)
            {
                continue;
            }

            var status = statusData?["status"]?.GetValue<string>() ?? "";

            if (status == "confirmed")
            {
                var token = statusData?["bot_token"]?.GetValue<string>();
                var baseUrl = statusData?["baseurl"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(token))
                {
                    _token = token;
                    if (!string.IsNullOrEmpty(baseUrl))
                        _config.BaseUrl = baseUrl;
                    SaveState();
                    _logger.LogInformation("WeiXin login successful!");
                    return true;
                }
                _logger.LogError("Login confirmed but no bot_token in response");
                return false;
            }

            if (status == "scaned")
            {
                _logger.LogInformation("QR code scanned, waiting for confirmation...");
            }
            else if (status == "expired")
            {
                refreshCount++;
                if (refreshCount > MaxQrRefreshCount)
                {
                    _logger.LogWarning("QR code expired too many times ({}/{}), giving up.", refreshCount - 1, MaxQrRefreshCount);
                    return false;
                }
                _logger.LogWarning("QR code expired, refreshing... ({}/{})", refreshCount, MaxQrRefreshCount);
                (qrcodeId, scanUrl) = await FetchQrCodeAsync(cancellationToken);
                PrintQrCode(scanUrl);
            }
        }
        return false;
    }

    private async Task<(string QrcodeId, string ScanUrl)> FetchQrCodeAsync(CancellationToken cancellationToken)
    {
        var data = await ApiGetAsync("ilink/bot/get_bot_qrcode", new() { ["bot_type"] = "3" }, auth: false)
            ?? throw new InvalidOperationException("Failed to get QR code");
        var qrcode = data["qrcode"]?.GetValue<string>() ?? "";
        var imgContent = data["qrcode_img_content"]?.GetValue<string>() ?? "";
        if (string.IsNullOrEmpty(qrcode)) throw new InvalidOperationException($"Failed to get QR code: {data}");
        return (qrcode, string.IsNullOrEmpty(imgContent) ? qrcode : imgContent);
    }

    private static void PrintQrCode(string url)
    {
        Console.WriteLine($"\n=== WeiXin QR Login ===");
        Console.WriteLine($"URL: {url}");
        Console.WriteLine("Scan the QR code above to link your WeChat account.\n");
    }

    // ==================================================================
    // Channel lifecycle
    // ==================================================================

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _running = true;
        _pollTimeoutS = _config.PollTimeout > 0 ? _config.PollTimeout : DefaultLongPollTimeoutS;

        if (!string.IsNullOrEmpty(_config.Token))
            _token = _config.Token;
        else if (string.IsNullOrEmpty(_token))
            LoadState();

        if (string.IsNullOrEmpty(_token))
        {
            if (!await QRLoginAsync(cancellationToken))
            {
                _logger.LogError("WeiXin login failed. Run 'nbot channels login weixin' to authenticate.");
                _running = false;
                return;
            }
        }

        _logger.LogInformation("WeiXin channel started (long-poll mode)");

        var consecutiveFailures = 0;
        while (_running && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(cancellationToken);
                consecutiveFailures = 0;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.RequestTimeout)
            {
                // Normal for long-poll
                continue;
            }
            catch (Exception ex) when (_running)
            {
                consecutiveFailures++;
                _logger.LogError("WeiXin poll error ({}/{}): {Error}", consecutiveFailures, MaxConsecutiveFailures, ex.Message);
                if (consecutiveFailures >= MaxConsecutiveFailures)
                {
                    consecutiveFailures = 0;
                    await Task.Delay(BackoffDelayS * 1000, cancellationToken);
                }
                else
                {
                    await Task.Delay(RetryDelayS * 1000, cancellationToken);
                }
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _running = false;
        SaveState();
        _logger.LogInformation("WeiXin channel stopped");
    }

    // ==================================================================
    // Polling
    // ==================================================================

    private void PauseSession(int durationS = SessionPauseDurationS)
    {
        _sessionPauseUntil = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 + durationS;
    }

    private int SessionPauseRemainingS()
    {
        var rem = _sessionPauseUntil - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        return rem > 0 ? (int)Math.Ceiling(rem) : 0;
    }

    private void AssertSessionActive()
    {
        var rem = SessionPauseRemainingS();
        if (rem > 0)
        {
            var min = (rem + 59) / 60;
            throw new InvalidOperationException($"WeiXin session paused, {min} min remaining (errcode {ErrSessionExpired})");
        }
    }

    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        var pauseRem = SessionPauseRemainingS();
        if (pauseRem > 0)
        {
            _logger.LogWarning("WeiXin session paused, waiting {Min} min before next poll.", (pauseRem + 59) / 60);
            await Task.Delay(pauseRem * 1000, cancellationToken);
            return;
        }

        var body = new JsonObject
        {
            ["get_updates_buf"] = JsonValue.Create(_updatesBuf)
        };

        AssertSessionActive();

        var data = await ApiPostAsync("ilink/bot/getupdates", body) ?? throw new InvalidOperationException("Null response from getupdates");

        var ret = data["ret"]?.GetValue<int>() ?? 0;
        var errcode = data["errcode"]?.GetValue<int>() ?? 0;
        var hasError = (ret != 0) || (errcode != 0);

        if (hasError)
        {
            if (errcode == ErrSessionExpired || ret == ErrSessionExpired)
            {
                PauseSession();
                var newRem = SessionPauseRemainingS();
                _logger.LogWarning("WeiXin session expired (errcode {Errcode}). Pausing {Min} min.", errcode, (newRem + 59) / 60);
                return;
            }
            throw new InvalidOperationException($"getUpdates failed: ret={ret} errcode={errcode} errmsg={data["errmsg"]}");
        }

        // Honour server-suggested poll timeout
        var serverTimeoutMs = data["longpolling_timeout_ms"]?.GetValue<int>() ?? 0;
        if (serverTimeoutMs > 0)
            _pollTimeoutS = Math.Max(serverTimeoutMs / 1000, 5);

        // Update cursor
        var newBuf = data["get_updates_buf"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(newBuf))
        {
            _updatesBuf = newBuf;
            SaveState();
        }

        // Process messages
        var msgs = data["msgs"]?.AsArray();
        if (msgs != null)
        {
            foreach (var msg in msgs)
            {
                try
                {
                    await ProcessMessageAsync(msg!.AsObject(), cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError("Error processing WeiXin message: {Error}", ex.Message);
                }
            }
        }
    }

    // ==================================================================
    // Inbound message processing
    // ==================================================================

    private async Task ProcessMessageAsync(JsonObject msg, CancellationToken cancellationToken)
    {
        // Skip bot's own messages
        var msgType = msg["message_type"]?.GetValue<int>() ?? 0;
        if (msgType == MsgTypeBot) return;

        // Deduplication
        var msgId = msg["message_id"]?.GetValue<string>()
            ?? msg["seq"]?.GetValue<string>()
            ?? $"{msg["from_user_id"]}_{msg["create_time_ms"]}";
        if (string.IsNullOrEmpty(msgId)) return;
        if (_processedIds.ContainsKey(msgId)) return;
        _processedIds[msgId] = true;
        while (_processedIds.Count > 1000) _processedIds.RemoveAt(0);

        var fromUserId = msg["from_user_id"]?.GetValue<string>() ?? "";
        if (string.IsNullOrEmpty(fromUserId)) return;

        // Cache context_token
        var ctxToken = msg["context_token"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(ctxToken))
        {
            _contextTokens[fromUserId] = ctxToken;
            SaveState();
        }

        // Parse item_list
        var items = msg["item_list"]?.AsArray();
        var contentParts = new List<string>();
        var mediaPaths = new List<string>();

        if (items != null)
        {
            foreach (var item in items)
            {
                var type = item?["type"]?.GetValue<int>() ?? 0;
                if (type == ItemTypeText)
                {
                    var text = item?["text_item"]?["text"]?.GetValue<string>() ?? "";
                    if (!string.IsNullOrEmpty(text))
                    {
                        var refMsg = item?["ref_msg"];
                        if (refMsg != null)
                        {
                            var refItem = refMsg?["message_item"];
                            var refType = refItem?["type"]?.GetValue<int>() ?? 0;
                            if (refType is >= 2 and <= 5)
                            {
                                contentParts.Add(text);
                            }
                            else
                            {
                                var parts = new List<string>();
                                if (refMsg?["title"] is { } title) parts.Add(title.GetValue<string>());
                                var refText = refItem?["text_item"]?["text"]?.GetValue<string>();
                                if (!string.IsNullOrEmpty(refText)) parts.Add(refText);
                                if (parts.Count > 0)
                                    contentParts.Add($"[引用: {string.Join(" | ", parts)}]\n{text}");
                                else
                                    contentParts.Add(text);
                            }
                        }
                        else
                        {
                            contentParts.Add(text);
                        }
                    }
                }
                else if (type == ItemTypeImage)
                {
                    var filePath = await DownloadMediaItemAsync(item!["image_item"]!.AsObject(), "image", cancellationToken);
                    if (!string.IsNullOrEmpty(filePath))
                    {
                        contentParts.Add($"[image]\n[Image: source: {filePath}]");
                        mediaPaths.Add(filePath);
                    }
                    else
                    {
                        contentParts.Add("[image]");
                    }
                }
                else if (type == ItemTypeVoice)
                {
                    var voiceItem = item!["voice_item"]!.AsObject();
                    var voiceText = voiceItem["text"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(voiceText))
                    {
                        contentParts.Add($"[voice] {voiceText}");
                    }
                    else
                    {
                        var filePath = await DownloadMediaItemAsync(voiceItem, "voice", cancellationToken);
                        if (!string.IsNullOrEmpty(filePath))
                        {
                            var transcription = await TranscribeAudioAsync(filePath, cancellationToken);
                            if (!string.IsNullOrEmpty(transcription))
                                contentParts.Add($"[voice] {transcription}");
                            else
                                contentParts.Add($"[voice]\n[Audio: source: {filePath}]");
                            mediaPaths.Add(filePath);
                        }
                        else
                        {
                            contentParts.Add("[voice]");
                        }
                    }
                }
                else if (type == ItemTypeFile)
                {
                    var fileItem = item!["file_item"]!.AsObject();
                    var fileName = fileItem["file_name"]?.GetValue<string>() ?? "unknown";
                    var filePath = await DownloadMediaItemAsync(fileItem, "file", cancellationToken);
                    if (!string.IsNullOrEmpty(filePath))
                    {
                        contentParts.Add($"[file: {fileName}]\n[File: source: {filePath}]");
                        mediaPaths.Add(filePath);
                    }
                    else
                    {
                        contentParts.Add($"[file: {fileName}]");
                    }
                }
                else if (type == ItemTypeVideo)
                {
                    var filePath = await DownloadMediaItemAsync(item!["video_item"]!.AsObject(), "video", cancellationToken);
                    if (!string.IsNullOrEmpty(filePath))
                    {
                        contentParts.Add($"[video]\n[Video: source: {filePath}]");
                        mediaPaths.Add(filePath);
                    }
                    else
                    {
                        contentParts.Add("[video]");
                    }
                }
            }
        }

        var content = string.Join("\n", contentParts);
        if (string.IsNullOrEmpty(content)) return;

        _logger.LogInformation("WeiXin inbound: from={FromUserId} msgId={MsgId}", fromUserId, msgId);

        await PublishInboundAsync(
            fromUserId,
            fromUserId,
            content,
            mediaPaths,
            new Dictionary<string, object> { ["message_id"] = msgId ?? "" }
        );
    }

    // ==================================================================
    // Media download (AES-128-ECB decryption)
    // ==================================================================

    private async Task<string?> DownloadMediaItemAsync(JsonObject item, string mediaType, CancellationToken cancellationToken)
    {
        try
        {
            var media = item["media"]?.AsObject();
            var encryptQueryParam = media?["encrypt_query_param"]?.GetValue<string>() ?? "";
            if (string.IsNullOrEmpty(encryptQueryParam)) return null;

            // Resolve AES key
            string? aesKey = null;
            var rawHexKey = item["aeskey"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(rawHexKey))
            {
                aesKey = Convert.ToBase64String(Convert.FromHexString(rawHexKey));
            }
            else
            {
                aesKey = media?["aes_key"]?.GetValue<string>();
            }

            // Build CDN download URL
            var cdnUrl = $"{_config.CdnBaseUrl.TrimEnd('/')}/download?encrypted_query_param={Uri.EscapeDataString(encryptQueryParam)}";
            var resp = await _httpClient.GetAsync(cdnUrl, cancellationToken);
            resp.EnsureSuccessStatusCode();
            var data = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

            if (!string.IsNullOrEmpty(aesKey) && data.Length > 0)
                data = AesEcbDecrypt(data, aesKey);

            if (data.Length == 0) return null;

            var ext = mediaType switch { "image" => ".jpg", "voice" => ".silk", "video" => ".mp4", _ => "" };
            var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var safeName = $"{mediaType}_{ts}{ext}";
            var filePath = Path.Combine(MediaDirectory, safeName);
            await File.WriteAllBytesAsync(filePath, data, cancellationToken);
            _logger.LogDebug("Downloaded WeiXin {MediaType} to {Path}", mediaType, filePath);
            return filePath;
        }
        catch (Exception ex)
        {
            _logger.LogError("Error downloading WeiXin media: {Error}", ex.Message);
            return null;
        }
    }

    private static byte[] AesEcbDecrypt(byte[] data, string aesKeyB64)
    {
        var key = ParseAesKey(aesKeyB64);
        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.PKCS7;
        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(data, 0, data.Length);
    }

    private static byte[] AesEcbEncrypt(byte[] data, string aesKeyB64)
    {
        var key = ParseAesKey(aesKeyB64);
        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.PKCS7;
        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(data, 0, data.Length);
    }

    private static byte[] ParseAesKey(string aesKeyB64)
    {
        var decoded = Convert.FromBase64String(aesKeyB64);
        if (decoded.Length == 16) return decoded;
        if (decoded.Length == 32 && Regex.IsMatch(Encoding.ASCII.GetString(decoded), "^[0-9a-fA-F]{32}$"))
            return Convert.FromHexString(Encoding.ASCII.GetString(decoded));
        throw new ArgumentException($"Invalid AES key length: expected 16 raw bytes or 32 hex chars, got {decoded.Length} bytes");
    }

    // ==================================================================
    // Outbound (send)
    // ==================================================================

    public override async Task SendMessageAsync(OutboundMessage message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_token))
        {
            _logger.LogWarning("WeiXin client not initialized or not authenticated");
            return;
        }

        try { AssertSessionActive(); }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("WeiXin send blocked: {Error}", ex.Message);
            return;
        }

        // Send media first
        if (message.Media?.Count > 0)
        {
            var ctxToken = _contextTokens.GetValueOrDefault(message.ChatId, "");
            foreach (var mediaPath in message.Media)
            {
                try
                {
                    await SendMediaFileAsync(message.ChatId, mediaPath, ctxToken, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError("Failed to send WeiXin media {Path}: {Error}", mediaPath, ex.Message);
                    await SendTextInternalAsync(message.ChatId, $"[Failed to send: {Path.GetFileName(mediaPath)}]", ctxToken, cancellationToken);
                }
            }
        }

        // Send text
        var content = message.Content?.Trim();
        if (string.IsNullOrEmpty(content)) return;

        var ctx = _contextTokens.GetValueOrDefault(message.ChatId, "");
        var chunks = SplitMessage(content, WeiXinMaxMsgLen);
        foreach (var chunk in chunks)
        {
            await SendTextInternalAsync(message.ChatId, chunk, ctx, cancellationToken);
        }
    }

    private async Task SendTextInternalAsync(string toUserId, string text, string contextToken, CancellationToken cancellationToken)
    {
        var clientId = $"nanobot-{Guid.NewGuid().ToString("N")[..12]}";
        var itemList = new JsonArray { new JsonObject { ["type"] = JsonValue.Create(ItemTypeText), ["text_item"] = new JsonObject { ["text"] = text } } };
        var weixinMsg = new JsonObject
        {
            ["from_user_id"] = "",
            ["to_user_id"] = toUserId,
            ["client_id"] = clientId,
            ["message_type"] = JsonValue.Create(MsgTypeBot),
            ["message_state"] = JsonValue.Create(MsgStateFinish),
            ["item_list"] = itemList
        };
        if (!string.IsNullOrEmpty(contextToken))
            weixinMsg["context_token"] = contextToken;

        var body = new JsonObject { ["msg"] = weixinMsg };

        var data = await ApiPostAsync("ilink/bot/sendmessage", body)
            ?? throw new InvalidOperationException("Null response from sendmessage");

        var errcode = data["errcode"]?.GetValue<int>() ?? 0;
        if (errcode != 0)
            _logger.LogWarning("WeiXin send error (code {Errcode}): {Error}", errcode, data["errmsg"] ?? "unknown");
    }

    private async Task SendMediaFileAsync(string toUserId, string mediaPath, string contextToken, CancellationToken cancellationToken)
    {
        var p = new FileInfo(mediaPath);
        if (!p.Exists) throw new FileNotFoundException($"Media file not found: {mediaPath}");

        var rawData = await File.ReadAllBytesAsync(mediaPath, cancellationToken);
        var rawMd5 = Convert.ToHexString(MD5.HashData(rawData)).ToLowerInvariant();
        var ext = p.Extension.ToLowerInvariant();
        var (uploadType, itemType, itemKey) = ext switch
        {
            _ when ImageExtensions.Contains(ext) => (UploadMediaTypeImage, ItemTypeImage, "image_item"),
            _ when VideoExtensions.Contains(ext) => (UploadMediaTypeVideo, ItemTypeVideo, "video_item"),
            _ => (UploadMediaTypeFile, ItemTypeFile, "file_item")
        };

        // Generate AES key
        var aesKeyRaw = RandomNumberGenerator.GetBytes(16);
        var aesKeyHex = Convert.ToHexString(aesKeyRaw).ToLowerInvariant();
        var aesKeyB64 = Convert.ToBase64String(Convert.FromHexString(aesKeyHex));

        // Step 1: Get upload URL
        var fileKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var paddedSize = ((rawData.Length + 1 + 15) / 16) * 16;
        var uploadBody = new JsonObject
        {
            ["filekey"] = fileKey,
            ["media_type"] = JsonValue.Create(uploadType),
            ["to_user_id"] = toUserId,
            ["rawsize"] = JsonValue.Create(rawData.Length),
            ["rawfilemd5"] = rawMd5,
            ["filesize"] = JsonValue.Create(paddedSize),
            ["no_need_thumb"] = true,
            ["aeskey"] = aesKeyHex
        };

        var uploadResp = await ApiPostAsync("ilink/bot/getuploadurl", uploadBody)
            ?? throw new InvalidOperationException("Null response from getuploadurl");
        var uploadParam = uploadResp["upload_param"]?.GetValue<string>()
            ?? throw new InvalidOperationException($"getuploadurl returned no upload_param: {uploadResp}");

        // Step 2: Encrypt and POST to CDN
        var encryptedData = AesEcbEncrypt(rawData, aesKeyB64);
        var cdnUrl = $"{_config.CdnBaseUrl.TrimEnd('/')}/upload?encrypted_query_param={Uri.EscapeDataString(uploadParam)}&filekey={Uri.EscapeDataString(fileKey)}";
        var cdnReq = new HttpRequestMessage(HttpMethod.Post, cdnUrl)
        {
            Content = new ByteArrayContent(encryptedData) { Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") } }
        };
        var cdnResp = await _httpClient.SendAsync(cdnReq, cancellationToken);
        cdnResp.EnsureSuccessStatusCode();
        var downloadParam = cdnResp.Headers.GetValues("x-encrypted-param").FirstOrDefault()
            ?? throw new InvalidOperationException("CDN upload response missing x-encrypted-param header");

        // Step 3: Send message with media item
        var cdnAesKeyB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(aesKeyHex));
        var mediaItem = new JsonObject
        {
            ["media"] = new JsonObject
            {
                ["encrypt_query_param"] = downloadParam,
                ["aes_key"] = cdnAesKeyB64,
                ["encrypt_type"] = 1
            }
        };
        if (itemType == ItemTypeImage) mediaItem["mid_size"] = JsonValue.Create(paddedSize);
        else if (itemType == ItemTypeVideo) mediaItem["video_size"] = JsonValue.Create(paddedSize);
        else if (itemType == ItemTypeFile) { mediaItem["file_name"] = p.Name; mediaItem["len"] = rawData.Length.ToString(); }

        var clientId = $"nanobot-{Guid.NewGuid().ToString("N")[..12]}";
        var weixinMsg = new JsonObject
        {
            ["from_user_id"] = "",
            ["to_user_id"] = toUserId,
            ["client_id"] = clientId,
            ["message_type"] = JsonValue.Create(MsgTypeBot),
            ["message_state"] = JsonValue.Create(MsgStateFinish),
            ["item_list"] = new JsonArray { new JsonObject { ["type"] = JsonValue.Create(itemType), [itemKey] = mediaItem } }
        };
        if (!string.IsNullOrEmpty(contextToken)) weixinMsg["context_token"] = contextToken;

        var body = new JsonObject { ["msg"] = weixinMsg };
        var data = await ApiPostAsync("ilink/bot/sendmessage", body)
            ?? throw new InvalidOperationException("Null response from sendmessage");
        var errcode = data["errcode"]?.GetValue<int>() ?? 0;
        if (errcode != 0)
            throw new InvalidOperationException($"WeiXin send media error (code {errcode}): {data["errmsg"] ?? "unknown"}");
        _logger.LogInformation("WeiXin media sent: {FileName} (type={ItemKey})", p.Name, itemKey);
    }

    // ==================================================================
    // Helpers
    // ==================================================================

    private static IEnumerable<string> SplitMessage(string content, int maxLen)
    {
        if (string.IsNullOrEmpty(content) || content.Length <= maxLen)
            yield return content;
        else
        {
            while (content.Length > 0)
            {
                if (content.Length <= maxLen)
                {
                    yield return content;
                    break;
                }
                var cut = content[..maxLen];
                var pos = cut.LastIndexOf('\n');
                if (pos == -1) pos = cut.LastIndexOf(' ');
                if (pos == -1) pos = maxLen;
                yield return content[..pos];
                content = content[pos..].TrimStart();
            }
        }
    }

    private static async Task<string?> TranscribeAudioAsync(string filePath, CancellationToken cancellationToken)
    {
        // TODO: Integrate with a speech-to-text service (e.g., OpenAI Whisper, Groq, etc.)
        await Task.CompletedTask;
        return null;
    }
}
