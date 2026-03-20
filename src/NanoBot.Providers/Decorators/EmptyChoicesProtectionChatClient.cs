using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace NanoBot.Providers.Decorators;

/// <summary>
/// 空 choices 保护装饰器 - 当 LLM 返回空 messages 时返回占位符响应，避免空响应导致的异常
/// </summary>
public class EmptyChoicesProtectionChatClient : IChatClient
{
    private readonly IChatClient _innerClient;
    private readonly ILogger<EmptyChoicesProtectionChatClient>? _logger;

    public EmptyChoicesProtectionChatClient(
        IChatClient innerClient,
        ILogger<EmptyChoicesProtectionChatClient>? logger = null)
    {
        _innerClient = innerClient ?? throw new ArgumentNullException(nameof(innerClient));
        _logger = logger;
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _innerClient.GetResponseAsync(messages, options, cancellationToken);

        // Handle empty messages
        if (!response.Messages.Any())
        {
            _logger?.LogWarning("Received empty messages from provider, returning placeholder response");

            return new ChatResponse
            {
                Messages =
                [
                    new ChatMessage(ChatRole.Assistant, string.Empty)
                ],
                Usage = response.Usage,
                ModelId = response.ModelId
            };
        }

        return response;
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in _innerClient.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return _innerClient.GetService(serviceType, serviceKey);
    }

    public void Dispose()
    {
        if (_innerClient is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
