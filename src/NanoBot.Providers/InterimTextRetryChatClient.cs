using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace NanoBot.Providers;

public interface IInterimTextRetryChatClient : IChatClient
{
}

public sealed class InterimTextRetryChatClient : IInterimTextRetryChatClient, IDisposable
{
    private readonly IChatClient _inner;
    private readonly ILogger? _logger;
    private bool _disposed;

    public InterimTextRetryChatClient(IChatClient inner, ILogger? logger = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _logger = logger;
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var messageList = chatMessages.ToList();
        var totalChars = messageList.Sum(m => m.Text?.Length ?? 0);
        LogPromptSize(messageList, options, "GetResponseAsync");

        var hasToolCalls = false;
        var textOnlyRetried = false;
        ChatResponse? response = null;

        while (true)
        {
            var reqSw = System.Diagnostics.Stopwatch.StartNew();
            response = await _inner.GetResponseAsync(messageList, options, cancellationToken);
            reqSw.Stop();
            _logger?.LogInformation("[TIMING] Inner GetResponseAsync completed in {Ms}ms", reqSw.ElapsedMilliseconds);

            var hasFunctionCalls = response.Messages.SelectMany(m => m.Contents)
                .Any(c => c is FunctionCallContent);

            if (hasFunctionCalls)
            {
                hasToolCalls = true;
                break;
            }

            var text = response.Messages.FirstOrDefault()?.Text;
            var cleanText = MessageSanitizer.StripThinkTags(text);

            var looksLikeThinkingModel = MessageSanitizer.ContainsThinkTags(text);
            if (!hasToolCalls && !textOnlyRetried && !string.IsNullOrEmpty(cleanText) && looksLikeThinkingModel)
            {
                textOnlyRetried = true;
                _logger?.LogDebug("Interim text with think tags (no tools used yet), retrying: {Preview}",
                    cleanText[..Math.Min(80, cleanText.Length)]);

                var assistantMessage = new ChatMessage(ChatRole.Assistant, text ?? string.Empty);
                messageList = [.. messageList, assistantMessage];
                continue;
            }

            break;
        }

        sw.Stop();
        _logger?.LogInformation("[TIMING] InterimTextRetryChatClient.GetResponseAsync total: {Ms}ms", sw.ElapsedMilliseconds);
        return response;
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var messageList = chatMessages.ToList();
        var totalChars = messageList.Sum(m => m.Text?.Length ?? 0);
        LogPromptSize(messageList, options, "GetStreamingResponseAsync");

        var firstChunk = true;
        await foreach (var update in _inner.GetStreamingResponseAsync(messageList, options, cancellationToken))
        {
            if (firstChunk)
            {
                firstChunk = false;
                sw.Stop();
                _logger?.LogInformation("[TIMING] First chunk from inner GetStreamingResponseAsync: {Ms}ms, text: {Text}", sw.ElapsedMilliseconds, update.Text);
            }
            yield return update;
        }
        sw.Stop();
        _logger?.LogInformation("[TIMING] InterimTextRetryChatClient.GetStreamingResponseAsync completed: {Ms}ms", sw.ElapsedMilliseconds);
    }

    public object? GetService(Type serviceType, object? key = null)
    {
        return _inner.GetService(serviceType, key);
    }

    private void LogPromptSize(List<ChatMessage> messages, ChatOptions? options, string method)
    {
        var messageChars = messages.Sum(m => m.Text?.Length ?? 0);
        var instructionChars = options?.Instructions?.Length ?? 0;
        var toolCount = options?.Tools?.Count ?? 0;
        var totalChars = messageChars + instructionChars;

        _logger?.LogInformation(
            "[PROMPT] {Method}: {MsgCount} messages ({MsgChars} chars) + instructions ({InstrChars} chars) + {ToolCount} tools = ~{TotalChars} total chars",
            method, messages.Count, messageChars, instructionChars, toolCount, totalChars);

        // Log per-message breakdown for diagnosis
        foreach (var msg in messages)
        {
            var len = msg.Text?.Length ?? 0;
            var preview = msg.Text?.Length > 80 ? msg.Text[..80] + "..." : msg.Text;
            _logger?.LogDebug("[PROMPT] {Role}: {Len} chars - {Preview}", msg.Role, len, preview);
        }
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
