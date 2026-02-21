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
        var messageList = chatMessages.ToList();
        var hasToolCalls = false;
        var textOnlyRetried = false;
        ChatResponse? response = null;

        while (true)
        {
            response = await _inner.GetResponseAsync(messageList, options, cancellationToken);

            var hasFunctionCalls = response.Messages.SelectMany(m => m.Contents)
                .Any(c => c is FunctionCallContent);

            if (hasFunctionCalls)
            {
                hasToolCalls = true;
                break;
            }

            var text = response.Messages.FirstOrDefault()?.Text;
            var cleanText = MessageSanitizer.StripThinkTags(text);

            if (!hasToolCalls && !textOnlyRetried && !string.IsNullOrEmpty(cleanText))
            {
                textOnlyRetried = true;
                _logger?.LogDebug("Interim text response (no tools used yet), retrying: {Preview}",
                    cleanText[..Math.Min(80, cleanText.Length)]);

                var assistantMessage = new ChatMessage(ChatRole.Assistant, text ?? string.Empty);
                messageList = [.. messageList, assistantMessage];
                continue;
            }

            break;
        }

        return response;
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in _inner.GetStreamingResponseAsync(chatMessages, options, cancellationToken))
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
