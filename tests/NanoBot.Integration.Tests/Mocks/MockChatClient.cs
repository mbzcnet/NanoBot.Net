using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace NanoBot.Integration.Tests;

public class MockChatClient : IChatClient
{
    private readonly Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>>? _responseGenerator;
    private readonly Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, IAsyncEnumerable<ChatResponseUpdate>>? _streamingResponseGenerator;
    private readonly List<ChatMessage> _receivedMessages = new();
    private int _callCount;

    public IReadOnlyList<ChatMessage> ReceivedMessages => _receivedMessages;
    public int CallCount => _callCount;
    public ChatClientMetadata Metadata { get; }

    public MockChatClient(string name = "mock", Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>>? responseGenerator = null, Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, IAsyncEnumerable<ChatResponseUpdate>>? streamingResponseGenerator = null)
    {
        Metadata = new ChatClientMetadata(name);
        _responseGenerator = responseGenerator;
        _streamingResponseGenerator = streamingResponseGenerator;
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
        Interlocked.Increment(ref _callCount);
        var messageList = messages.ToList();
        lock (_receivedMessages)
        {
            _receivedMessages.AddRange(messageList);
        }

        string responseText;
        if (_streamingResponseGenerator != null)
        {
            await foreach (var update in _streamingResponseGenerator(messageList, options, cancellationToken))
            {
                yield return update;
            }
            yield break;
        }

        if (_responseGenerator != null)
        {
            var response = await _responseGenerator(messageList, options, cancellationToken);
            responseText = response.Messages.FirstOrDefault()?.Text ?? $"Mock response #{_callCount}";
        }
        else
        {
            responseText = $"Mock response #{_callCount}";
        }

        // Yield text in chunks to simulate realistic streaming
        var chunks = responseText.Split(' ');
        for (int i = 0; i < chunks.Length; i++)
        {
            var chunk = i < chunks.Length - 1 ? chunks[i] + " " : chunks[i];
            yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
        }
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
