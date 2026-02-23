using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text;

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
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var messageList = chatMessages.ToList();
        var sanitized = MessageSanitizer.SanitizeMessages(messageList);
        _logger?.LogDebug("Sanitized {Count} messages for strict provider (streaming)", messageList.Count);

        var totalChars = messageList.Sum(m => m.Text?.Length ?? 0);
        var instructionsChars = options?.Instructions?.Length ?? 0;
        _logger?.LogInformation("[DEBUG] Streaming request - {MsgCount} messages, {TotalChars} chars total, Instructions: {InstChars} chars",
            messageList.Count, totalChars, instructionsChars);

        var requestDir = Path.Combine(Path.GetTempPath(), "nanobot_requests");
        Directory.CreateDirectory(requestDir);
        var requestFile = Path.Combine(requestDir, $"req_{DateTime.Now:yyyyMMdd_HHmmss_fff}.txt");
        
        var requestContent = new StringBuilder();
        requestContent.AppendLine($"=== Request at {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ===");
        requestContent.AppendLine($"Messages: {messageList.Count}, Total chars: {totalChars}, Instructions: {instructionsChars}");
        requestContent.AppendLine();
        
        for (int i = 0; i < messageList.Count; i++)
        {
            var msg = messageList[i];
            requestContent.AppendLine($"--- Message {i}: {msg.Role} ---");
            requestContent.AppendLine(msg.Text ?? "(empty)");
            requestContent.AppendLine();
        }
        
        if (!string.IsNullOrEmpty(options?.Instructions))
        {
            requestContent.AppendLine("--- ChatOptions.Instructions ---");
            requestContent.AppendLine(options.Instructions);
            requestContent.AppendLine();
        }
        
        await File.WriteAllTextAsync(requestFile, requestContent.ToString(), cancellationToken);
        _logger?.LogInformation("[DEBUG] Request saved to: {RequestFile}", requestFile);

        for (int i = 0; i < messageList.Count; i++)
        {
            var msg = messageList[i];
            var preview = msg.Text?.Length > 100 ? msg.Text[..100] + "..." : msg.Text;
            _logger?.LogInformation("[DEBUG] Message {Idx}: role={Role}, content={Content}",
                i, msg.Role, preview);
        }

        if (!string.IsNullOrEmpty(options?.Instructions))
        {
            var instPreview = options.Instructions.Length > 200 ? options.Instructions[..200] + "..." : options.Instructions;
            _logger?.LogInformation("[DEBUG] ChatOptions.Instructions (first 500 chars): {Inst}", instPreview);
        }

        _logger?.LogInformation("[TIMING] SanitizingChatClient.GetStreamingResponseAsync start");
        var firstChunk = true;
        await foreach (var update in _inner.GetStreamingResponseAsync(sanitized, options, cancellationToken))
        {
            if (firstChunk)
            {
                firstChunk = false;
                sw.Stop();
                _logger?.LogInformation("[TIMING] First chunk from inner client: {Ms}ms", sw.ElapsedMilliseconds);
            }
            yield return update;
        }
        sw.Stop();
        _logger?.LogInformation("[TIMING] SanitizingChatClient.GetStreamingResponseAsync completed: {Ms}ms", sw.ElapsedMilliseconds);
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

    /// <summary>
    /// Returns true if the text contains <think> tags (thinking model output).
    /// </summary>
    public static bool ContainsThinkTags(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        return System.Text.RegularExpressions.Regex.IsMatch(
            text, @"<think[\s\S]*?</think\s*>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
