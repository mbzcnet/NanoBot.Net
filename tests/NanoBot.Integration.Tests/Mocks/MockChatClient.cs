using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace NanoBot.Integration.Tests;

public class MockChatClient : IChatClient
{
    private readonly Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>>? _responseGenerator;
    private readonly List<ChatMessage> _receivedMessages = new();
    private int _callCount;

    public IReadOnlyList<ChatMessage> ReceivedMessages => _receivedMessages;
    public int CallCount => _callCount;
    public ChatClientMetadata Metadata { get; }

    public MockChatClient(string name = "mock", Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>>? responseGenerator = null)
    {
        Metadata = new ChatClientMetadata(name);
        _responseGenerator = responseGenerator;
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        var messageList = messages.ToList();
        lock (_receivedMessages)
        {
            _receivedMessages.AddRange(messageList);
        }

        if (_responseGenerator != null)
        {
            return await _responseGenerator(messageList, options, cancellationToken);
        }

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, $"Mock response #{_callCount}"));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        yield return new ChatResponseUpdate(
            ChatRole.Assistant,
            response.Messages.FirstOrDefault()?.Text ?? string.Empty);
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(ChatClientMetadata))
        {
            return Metadata;
        }

        return null;
    }

    public void Dispose()
    {
    }
}
