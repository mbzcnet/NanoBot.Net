using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NanoBot.Channels.Abstractions;
using NanoBot.Core.Bus;
using NanoBot.Core.Configuration;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using ApiException = Telegram.Bot.Exceptions.ApiRequestException;

namespace NanoBot.Channels.Implementations.Telegram;

public partial class TelegramChannel : ChannelBase
{
    public override string Id => "telegram";
    public override string Type => "telegram";

    private readonly TelegramConfig _config;
    private TelegramBotClient? _botClient;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _typingTasks = new();
    private User? _botUser;

    // Media group aggregation
    private readonly ConcurrentDictionary<string, MediaGroupBuffer> _mediaGroupBuffers = new();
    private readonly ConcurrentDictionary<string, Task> _mediaGroupTasks = new();
    private readonly TimeSpan _mediaGroupDelay = TimeSpan.FromMilliseconds(600);

    private sealed class MediaGroupBuffer
    {
        public string SenderId { get; set; } = "";
        public string ChatId { get; set; } = "";
        public List<string> Contents { get; set; } = new();
        public List<string> Media { get; set; } = new();
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    public override IDictionary<string, object?>? DefaultConfig()
    {
        return new Dictionary<string, object?>
        {
            ["enabled"] = false,
            ["token"] = "",
            ["allowFrom"] = Array.Empty<string>(),
            ["replyToMessage"] = false
        };
    }

    public TelegramChannel(TelegramConfig config, IMessageBus bus, ILogger<TelegramChannel> logger)
        : base(bus, logger)
    {
        _config = config;
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_config.Token))
        {
            _logger.LogError("Telegram bot token not configured");
            return;
        }

        _running = true;

        try
        {
            _botClient = new TelegramBotClient(_config.Token);

            _botUser = await _botClient.GetMeAsync(cancellationToken);
            _logger.LogInformation("Telegram bot connected: @{Username}", _botUser?.Username);

            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = new[] { UpdateType.Message }
            };

            _botClient.StartReceiving(
                HandleUpdateAsync,
                HandleErrorAsync,
                receiverOptions,
                cancellationToken
            );

            _logger.LogInformation("Telegram channel started (polling mode)");

            while (_running && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Telegram channel");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _running = false;

        // Cancel all typing indicators
        foreach (var cts in _typingTasks.Values)
        {
            cts.Cancel();
        }
        _typingTasks.Clear();

        // Cancel media group tasks
        foreach (var kvp in _mediaGroupTasks.ToList())
        {
            try
            {
                if (!_mediaGroupBuffers.ContainsKey(kvp.Key))
                {
                    continue;
                }
            }
            catch { }
        }
        _mediaGroupTasks.Clear();
        _mediaGroupBuffers.Clear();

        _logger.LogInformation("Telegram channel stopped");
        await Task.CompletedTask;
    }

    public override async Task SendMessageAsync(OutboundMessage message, CancellationToken cancellationToken = default)
    {
        if (_botClient == null)
        {
            _logger.LogWarning("Telegram bot not running");
            return;
        }

        StopTyping(message.ChatId);

        if (!long.TryParse(message.ChatId, out var chatId))
        {
            _logger.LogError("Invalid chat_id: {ChatId}", message.ChatId);
            return;
        }

        var replyToMessageId = GetReplyToMessageId(message);

        foreach (var chunk in SplitMessage(message.Content))
        {
            try
            {
                var html = MarkdownToTelegramHtml(chunk);
                await SendTextMessageWithRetryAsync(
                    chatId,
                    html,
                    replyToMessageId,
                    parseMode: ParseMode.Html,
                    cancellationToken: cancellationToken);
            }
            catch (ApiRequestException ex) when (ex.Message.Contains("can't parse entities"))
            {
                _logger.LogWarning(ex, "HTML parse failed, falling back to plain text");
                try
                {
                    await SendTextMessageWithRetryAsync(
                        chatId,
                        chunk,
                        replyToMessageId,
                        parseMode: null,
                        cancellationToken: cancellationToken);
                }
                catch (Exception ex2)
                {
                    _logger.LogError(ex2, "Error sending Telegram message");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending Telegram message");
            }
        }
    }

    private int? GetReplyToMessageId(OutboundMessage message)
    {
        if (!_config.ReplyToMessage) return null;

        if (message.Metadata != null &&
            message.Metadata.TryGetValue("message_id", out var msgIdObj) &&
            msgIdObj is int msgId)
        {
            return msgId;
        }

        return null;
    }

    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        if (update.Message is not { } message)
            return;

        if (message.From is not { } user)
            return;

        var senderId = BuildSenderId(user);
        var chatId = message.Chat.Id.ToString();
        var strChatId = chatId;

        var contentParts = new List<string>();

        if (!string.IsNullOrEmpty(message.Text))
            contentParts.Add(message.Text);

        if (!string.IsNullOrEmpty(message.Caption))
            contentParts.Add(message.Caption);

        var content = string.Join("\n", contentParts);

        if (content.Trim().ToLowerInvariant() == "/help")
        {
            await SendHelpMessageAsync(chatId, cancellationToken);
            return;
        }

        if (!IsAllowed(senderId, _config.AllowFrom))
        {
            _logger.LogWarning("Access denied for sender {SenderId} on Telegram channel", senderId);
            return;
        }

        var mediaPaths = new List<string>();
        var mediaType = (string?)null;

        // Download media if present
        if (message.Photo != null || message.Voice != null || message.Audio != null || message.Document != null)
        {
            var (fileId, type) = message.Photo != null ? (message.Photo.Last().FileId, "image") :
                                 message.Voice != null ? (message.Voice.FileId, "voice") :
                                 message.Audio != null ? (message.Audio.FileId, "audio") :
                                 message.Document != null ? (message.Document.FileId, "file") :
                                 (null, null);

            if (fileId != null && type != null)
            {
                mediaType = type;
                try
                {
                    var filePath = await DownloadFileAsync(fileId, cancellationToken);
                    if (filePath != null)
                    {
                        mediaPaths.Add(filePath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to download media");
                }
            }
        }

        if (!string.IsNullOrEmpty(message.Text))
            contentParts.Add(message.Text);

        if (!string.IsNullOrEmpty(message.Caption))
            contentParts.Add(message.Caption);

        if (mediaType != null)
        {
            contentParts.Add($"[{mediaType}: attachment]");
        }

        content = string.Join("\n", contentParts);
        if (string.IsNullOrEmpty(content))
            content = "[empty message]";

        // Media group aggregation: buffer briefly, forward as one aggregated turn
        if (!string.IsNullOrEmpty(message.MediaGroupId))
        {
            var key = $"{strChatId}:{message.MediaGroupId}";

            var buffer = _mediaGroupBuffers.GetOrAdd(key, _ => new MediaGroupBuffer
            {
                SenderId = senderId,
                ChatId = strChatId,
                Contents = new List<string>(),
                Media = new List<string>(),
                Metadata = new Dictionary<string, object>
                {
                    ["message_id"] = message.MessageId,
                    ["user_id"] = user.Id,
                    ["username"] = user.Username ?? "",
                    ["first_name"] = user.FirstName,
                    ["is_group"] = message.Chat.Type != ChatType.Private,
                    ["media_group_id"] = message.MediaGroupId ?? ""
                }
            });

            StartTyping(strChatId);

            if (!string.IsNullOrEmpty(content) && content != "[empty message]")
            {
                buffer.Contents.Add(content);
            }

            if (mediaPaths.Count > 0)
            {
                buffer.Media.AddRange(mediaPaths);
            }

            if (!_mediaGroupTasks.ContainsKey(key))
            {
                _mediaGroupTasks[key] = FlushMediaGroupAsync(key);
            }

            return;
        }

        // Start typing indicator before processing
        StartTyping(chatId);

        await HandleMessageAsync(
            senderId,
            chatId,
            content,
            mediaPaths,
            new Dictionary<string, object>
            {
                ["message_id"] = message.MessageId,
                ["user_id"] = user.Id,
                ["username"] = user.Username ?? "",
                ["first_name"] = user.FirstName,
                ["is_group"] = message.Chat.Type != ChatType.Private
            }
        );
    }

    private async Task FlushMediaGroupAsync(string key)
    {
        try
        {
            await Task.Delay(_mediaGroupDelay);

            if (!_mediaGroupBuffers.TryRemove(key, out var buffer))
            {
                return;
            }

            var content = string.Join("\n", buffer.Contents);
            if (string.IsNullOrEmpty(content))
            {
                content = "[empty message]";
            }
            var dedupedMedia = buffer.Media.Distinct().ToList();

            await HandleMessageAsync(
                senderId: buffer.SenderId,
                chatId: buffer.ChatId,
                content: content,
                media: dedupedMedia,
                metadata: buffer.Metadata
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error flushing media group {Key}", key);
        }
        finally
        {
            _mediaGroupTasks.TryRemove(key, out _);
        }
    }

    private async Task<string?> DownloadFileAsync(string fileId, CancellationToken cancellationToken)
    {
        if (_botClient == null)
        {
            return null;
        }

        var file = await _botClient.GetFileAsync(fileId, cancellationToken);
        if (file.FilePath == null)
        {
            return null;
        }

        var fileName = Path.GetFileName(file.FilePath);
        var filePath = Path.Combine(_mediaDirectory, fileName);

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await _botClient.DownloadFileAsync(file.FilePath, stream, cancellationToken);

        return filePath;
    }

    private readonly string _mediaDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".nanobot",
        "media");

    private async Task SendHelpMessageAsync(string chatId, CancellationToken cancellationToken)
    {
        if (_botClient == null || !long.TryParse(chatId, out var id)) return;

        var helpText = @"🐈 nanobot commands:
/new — Start a new conversation
/help — Show available commands";

        try
        {
            await _botClient.SendTextMessageAsync(id, helpText, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending help message to Telegram");
        }
    }

    private Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Telegram polling error");
        return Task.CompletedTask;
    }

    private static string BuildSenderId(User user)
    {
        var sid = user.Id.ToString();
        return !string.IsNullOrEmpty(user.Username) ? $"{sid}|{user.Username}" : sid;
    }

    private void StartTyping(string chatId)
    {
        StopTyping(chatId);

        var cts = new CancellationTokenSource();
        _typingTasks[chatId] = cts;

        _ = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested && _botClient != null)
            {
                try
                {
                    if (long.TryParse(chatId, out var id))
                    {
                        await _botClient.SendChatActionAsync(id, ChatAction.Typing, cancellationToken: cts.Token);
                    }
                }
                catch { }
                await Task.Delay(4000, cts.Token);
            }
        }, cts.Token);
    }

    private void StopTyping(string chatId)
    {
        if (_typingTasks.TryRemove(chatId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    private static IEnumerable<string> SplitMessage(string content, int maxLen = 4000)
    {
        if (string.IsNullOrEmpty(content) || content.Length <= maxLen)
        {
            yield return content;
            yield break;
        }

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

    private static string MarkdownToTelegramHtml(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var codeBlocks = new List<string>();
        text = CodeBlockRegex().Replace(text, m =>
        {
            codeBlocks.Add(m.Groups[1].Value);
            return $"\x00CB{codeBlocks.Count - 1}\x00";
        });

        var inlineCodes = new List<string>();
        text = InlineCodeRegex().Replace(text, m =>
        {
            inlineCodes.Add(m.Groups[1].Value);
            return $"\x00IC{inlineCodes.Count - 1}\x00";
        });

        text = HeaderRegex().Replace(text, "$1");
        text = BlockquoteRegex().Replace(text, "$1");

        text = text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        text = LinkRegex().Replace(text, @"<a href=""$2"">$1</a>");
        text = BoldRegex().Replace(text, @"<b>$1</b>");
        text = BoldUnderscoreRegex().Replace(text, @"<b>$1</b>");
        text = ItalicRegex().Replace(text, @"<i>$1</i>");
        text = StrikethroughRegex().Replace(text, @"<s>$1</s>");
        text = BulletRegex().Replace(text, "• ");

        for (var i = 0; i < inlineCodes.Count; i++)
        {
            var escaped = inlineCodes[i].Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
            text = text.Replace($"\x00IC{i}\x00", $"<code>{escaped}</code>");
        }

        for (var i = 0; i < codeBlocks.Count; i++)
        {
            var escaped = codeBlocks[i].Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
            text = text.Replace($"\x00CB{i}\x00", $"<pre><code>{escaped}</code></pre>");
        }

        return text;
    }

    [GeneratedRegex(@"```[\w]*\n?([\s\S]*?)```")]
    private static partial Regex CodeBlockRegex();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex(@"^#{1,6}\s+(.+)$", RegexOptions.Multiline)]
    private static partial Regex HeaderRegex();

    [GeneratedRegex(@"^>\s*(.*)$", RegexOptions.Multiline)]
    private static partial Regex BlockquoteRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)]+)\)")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex BoldRegex();

    [GeneratedRegex(@"__(.+?)__")]
    private static partial Regex BoldUnderscoreRegex();

    [GeneratedRegex(@"(?<![a-zA-Z0-9])_([^_]+)_(?![a-zA-Z0-9])")]
    private static partial Regex ItalicRegex();

    [GeneratedRegex(@"~~(.+?)~~")]
    private static partial Regex StrikethroughRegex();

    [GeneratedRegex(@"^[-*]\s+", RegexOptions.Multiline)]
    private static partial Regex BulletRegex();

    /// <summary>
    /// Executes an async Telegram API call with exponential backoff retry
    /// </summary>
    private async Task CallWithRetryAsync(
        Func<Task> func,
        int maxRetries = 3,
        CancellationToken cancellationToken = default)
    {
        var delay = TimeSpan.FromSeconds(1);
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                await func();
                return;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt < maxRetries - 1)
                {
                    _logger.LogWarning("Telegram API call timed out, retrying in {Delay}s (attempt {Attempt}/{MaxRetries})",
                        delay.TotalSeconds, attempt + 1, maxRetries);
                    await Task.Delay(delay, cancellationToken);
                    delay *= 2;
                }
            }
            catch (HttpRequestException ex) when (attempt < maxRetries - 1)
            {
                _logger.LogWarning(ex, "Telegram API call failed, retrying in {Delay}s (attempt {Attempt}/{MaxRetries})",
                    delay.TotalSeconds, attempt + 1, maxRetries);
                await Task.Delay(delay, cancellationToken);
                delay *= 2;
            }
            catch (ApiException ex) when (ex.ErrorCode == 429 && attempt < maxRetries - 1)
            {
                var retryAfter = ex.Parameters?.RetryAfter;
                delay = retryAfter.HasValue
                    ? TimeSpan.FromSeconds(Math.Max(retryAfter.Value, delay.TotalSeconds))
                    : delay * 2;
                _logger.LogWarning("Telegram rate limited, retrying in {Delay}s", delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
            }
        }

        // Final attempt
        await func();
    }

    /// <summary>
    /// Sends a message with retry support
    /// </summary>
    private async Task SendTextMessageWithRetryAsync(
        long chatId,
        string text,
        int? replyToMessageId = null,
        ParseMode? parseMode = null,
        CancellationToken cancellationToken = default)
    {
        await CallWithRetryAsync(async () =>
        {
            await _botClient!.SendTextMessageAsync(
                chatId,
                text,
                parseMode: parseMode ?? ParseMode.Html,
                replyToMessageId: replyToMessageId,
                cancellationToken: cancellationToken);
        }, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Detects if media path is a remote URL
    /// </summary>
    private bool IsRemoteUrl(string path)
    {
        return path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetExtensionFromContentType(string contentType)
    {
        return contentType.ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "video/mp4" => ".mp4",
            "video/quicktime" => ".mov",
            "audio/mpeg" or "audio/mp3" => ".mp3",
            "audio/ogg" => ".ogg",
            "audio/wav" => ".wav",
            "application/pdf" => ".pdf",
            _ => ""
        };
    }
}
