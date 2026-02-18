using System.Collections.Concurrent;
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

namespace NanoBot.Channels.Implementations.Telegram;

public partial class TelegramChannel : ChannelBase
{
    public override string Id => "telegram";
    public override string Type => "telegram";

    private readonly TelegramConfig _config;
    private TelegramBotClient? _botClient;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _typingTasks = new();
    private User? _botUser;

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

        foreach (var cts in _typingTasks.Values)
        {
            cts.Cancel();
        }
        _typingTasks.Clear();

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

        foreach (var chunk in SplitMessage(message.Content))
        {
            try
            {
                var html = MarkdownToTelegramHtml(chunk);
                await _botClient.SendTextMessageAsync(chatId, html, parseMode: ParseMode.Html, cancellationToken: cancellationToken);
            }
            catch (ApiRequestException ex)
            {
                _logger.LogWarning(ex, "HTML parse failed, falling back to plain text");
                try
                {
                    await _botClient.SendTextMessageAsync(chatId, chunk, cancellationToken: cancellationToken);
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

    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        if (update.Message is not { } message)
            return;

        if (message.From is not { } user)
            return;

        var senderId = BuildSenderId(user);
        var chatId = message.Chat.Id.ToString();

        if (!IsAllowed(senderId, _config.AllowFrom))
        {
            _logger.LogWarning("Access denied for sender {SenderId} on Telegram channel", senderId);
            return;
        }

        var contentParts = new List<string>();
        var mediaPaths = new List<string>();

        if (!string.IsNullOrEmpty(message.Text))
            contentParts.Add(message.Text);

        if (!string.IsNullOrEmpty(message.Caption))
            contentParts.Add(message.Caption);

        var hasMedia = message.Photo != null || message.Voice != null || message.Audio != null || message.Document != null;
        var mediaType = message.Photo != null ? "image" :
                        message.Voice != null ? "voice" :
                        message.Audio != null ? "audio" :
                        message.Document != null ? "file" : null;

        if (hasMedia && mediaType != null)
        {
            contentParts.Add($"[{mediaType}: attachment]");
        }

        var content = string.Join("\n", contentParts);
        if (string.IsNullOrEmpty(content))
            content = "[empty message]";

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
                ["username"] = user.Username,
                ["first_name"] = user.FirstName,
                ["is_group"] = message.Chat.Type != ChatType.Private
            }
        );
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
}
