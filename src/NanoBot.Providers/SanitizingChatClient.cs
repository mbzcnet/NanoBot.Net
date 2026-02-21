using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace NanoBot.Providers;

public interface ISanitizingChatClient : IChatClient
{
}

public sealed class SanitizingChatClient : ISanitizingChatClient, IDisposable
{
    private readonly IChatClient _inner;
    private readonly ILogger? _logger;
    private bool _disposed;

    public SanitizingChatClient(IChatClient inner, ILogger? logger = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _logger = logger;
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var messageList = chatMessages.ToList();
        var sanitized = MessageSanitizer.SanitizeMessages(messageList);
        _logger?.LogDebug("Sanitized {Count} messages for strict provider", messageList.Count);
        return await _inner.GetResponseAsync(sanitized, options, cancellationToken);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messageList = chatMessages.ToList();
        var sanitized = MessageSanitizer.SanitizeMessages(messageList);
        _logger?.LogDebug("Sanitized {Count} messages for strict provider (streaming)", messageList.Count);

        await foreach (var update in _inner.GetStreamingResponseAsync(sanitized, options, cancellationToken))
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? key = null)
    {
        return _inner.GetService(serviceType, key);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_inner is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

public static class MessageSanitizer
{
    public static IList<ChatMessage> SanitizeMessages(IList<ChatMessage> messages)
    {
        if (messages == null || messages.Count == 0)
            return messages;

        var sanitized = new List<ChatMessage>(messages.Count);

        foreach (var msg in messages)
        {
            var cleanMessage = SanitizeMessage(msg);
            sanitized.Add(cleanMessage);
        }

        return sanitized;
    }

    private static ChatMessage SanitizeMessage(ChatMessage msg)
    {
        var text = msg.Text;
        var role = msg.Role;

        if (role == ChatRole.Assistant)
        {
            if (string.IsNullOrEmpty(text) && !HasFunctionCalls(msg))
            {
                text = string.Empty;
            }
        }

        var cleanMessage = new ChatMessage(role, text ?? string.Empty);

        foreach (var content in msg.Contents)
        {
            if (content is FunctionCallContent fcc)
            {
                cleanMessage.Contents.Add(fcc);
            }
            else if (content is FunctionResultContent frc)
            {
                cleanMessage.Contents.Add(frc);
            }
        }

        return cleanMessage;
    }

    private static bool HasFunctionCalls(ChatMessage msg)
    {
        return msg.Contents.Any(c => c is FunctionCallContent);
    }

    public static string StripThinkTags(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var result = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"<think[\s\S]*?</think\s*>",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return result.Trim();
    }
}
