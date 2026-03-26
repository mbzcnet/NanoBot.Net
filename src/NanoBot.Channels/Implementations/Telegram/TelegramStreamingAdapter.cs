using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Channels.Adapters;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;

namespace NanoBot.Channels.Implementations.Telegram;

/// <summary>
/// Streaming adapter for Telegram that supports streaming message updates.
/// </summary>
public class TelegramStreamingAdapter : IChannelStreamingAdapter
{
    private readonly TelegramBotClient _client;
    private readonly ILogger<TelegramStreamingAdapter>? _logger;
    private readonly ConcurrentDictionary<string, TelegramStreamState> _streamStates;
    private readonly int _maxEditDelayMs;

    private class TelegramStreamState
    {
        public long? MessageId { get; set; }
        public string Text { get; set; } = string.Empty;
        public DateTime LastEdit { get; set; }
        public string StreamId { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    public TelegramStreamingAdapter(TelegramBotClient client, ILogger<TelegramStreamingAdapter>? logger = null)
    {
        _client = client;
        _logger = logger;
        _streamStates = new ConcurrentDictionary<string, TelegramStreamState>();
        _maxEditDelayMs = 500; // Edit message at most every 500ms to avoid rate limiting
    }

    public async IAsyncEnumerable<string> StreamMessageAsync(
        string chatId,
        string content,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var streamId = $"{chatId}:{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var state = new TelegramStreamState
        {
            StreamId = streamId,
            IsActive = true,
            LastEdit = DateTime.UtcNow
        };
        _streamStates[chatId] = state;

        var buffer = new StringBuilder();
        var lastYieldIndex = 0;
        var lastEditTime = DateTime.UtcNow;

        foreach (var c in content)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                state.IsActive = false;
                yield break;
            }

            buffer.Append(c);

            // Check if we should send an update
            var timeSinceLastEdit = (DateTime.UtcNow - lastEditTime).TotalMilliseconds;
            var bufferSize = buffer.Length - lastYieldIndex;

            // Send update if enough time has passed or buffer is large enough
            if (timeSinceLastEdit >= _maxEditDelayMs || bufferSize >= 50)
            {
                var partialText = buffer.ToString();
                state.Text = partialText;

                if (state.MessageId == null)
                {
                    // First message - send initial message
                    try
                    {
                        var msg = await _client.SendTextMessageAsync(
                            chatId,
                            partialText,
                            cancellationToken: cancellationToken);
                        state.MessageId = msg.MessageId;
                        lastEditTime = DateTime.UtcNow;
                        _logger?.LogDebug("Telegram streaming: sent initial message {MessageId}", msg.MessageId);
                    }
                    catch (ApiRequestException ex) when (ex.Message.Contains("MESSAGE_TOO_LONG"))
                    {
                        // Message is too long, try to split or truncate
                        _logger?.LogWarning("Telegram streaming: message too long, truncating");
                        state.IsActive = false;
                        yield break;
                    }
                }
                else
                {
                    // Edit existing message
                    try
                    {
                        await _client.EditMessageTextAsync(
                            chatId,
                            (int)state.MessageId,
                            partialText,
                            cancellationToken: cancellationToken);
                        lastEditTime = DateTime.UtcNow;
                    }
                    catch (ApiRequestException ex) when (ex.Message.Contains("message is not modified") ||
                                                        ex.Message.Contains("ChatBoostNotModified"))
                    {
                        // Ignore "not modified" errors
                        lastEditTime = DateTime.UtcNow;
                    }
                    catch (ApiRequestException ex) when (ex.Message.Contains("MESSAGE_TOO_LONG"))
                    {
                        _logger?.LogWarning("Telegram streaming: edited message too long");
                        state.IsActive = false;
                        yield break;
                    }
                }

                // Yield the partial text
                for (int i = lastYieldIndex; i < buffer.Length; i++)
                {
                    yield return buffer[i].ToString();
                }
                lastYieldIndex = buffer.Length;
            }
        }

        // Yield any remaining characters
        if (lastYieldIndex < buffer.Length)
        {
            var remaining = buffer.ToString(lastYieldIndex, buffer.Length - lastYieldIndex);
            foreach (var c in remaining)
            {
                yield return c.ToString();
            }
        }

        state.IsActive = false;
        _logger?.LogDebug("Telegram streaming: completed for {StreamId}", streamId);
    }

    public async Task FinalizeStreamAsync(string chatId, CancellationToken cancellationToken = default)
    {
        if (_streamStates.TryRemove(chatId, out var state))
        {
            state.IsActive = false;
            _logger?.LogDebug("Telegram streaming: finalized stream {StreamId}, final text length: {Length}",
                state.StreamId, state.Text?.Length ?? 0);
        }

        await Task.CompletedTask;
    }
}
