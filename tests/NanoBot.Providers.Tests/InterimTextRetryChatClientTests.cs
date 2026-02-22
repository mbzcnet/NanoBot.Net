using Microsoft.Extensions.AI;
using Moq;
using NanoBot.Providers;
using Xunit;

namespace NanoBot.Providers.Tests;

public class InterimTextRetryChatClientTests
{
    [Fact]
    public void InterimTextRetryChatClient_ImplementsIChatClient()
    {
        var mockInner = new Mock<Microsoft.Extensions.AI.IChatClient>();
        var client = new InterimTextRetryChatClient(mockInner.Object);

        Assert.IsAssignableFrom<Microsoft.Extensions.AI.IChatClient>(client);
    }

    [Fact]
    public void InterimTextRetryChatClient_DisposesInnerClient()
    {
        var mockInner = new Mock<Microsoft.Extensions.AI.IChatClient>();
        var disposableInner = mockInner.As<IDisposable>();
        var client = new InterimTextRetryChatClient(mockInner.Object);

        client.Dispose();

        disposableInner.Verify(x => x.Dispose(), Times.Once);
    }

    [Fact]
    public async Task GetResponseAsync_WithToolCalls_ReturnsResponseWithoutRetry()
    {
        var mockInner = new Mock<Microsoft.Extensions.AI.IChatClient>();
        var toolCallContent = new FunctionCallContent("search", "call123", new Dictionary<string, object?> { ["query"] = "test" });
        
        var responseMessage = new ChatMessage(ChatRole.Assistant, "Here are the results");
        responseMessage.Contents.Add(toolCallContent);
        
        mockInner.Setup(x => x.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(responseMessage));

        var client = new InterimTextRetryChatClient(mockInner.Object);

        var result = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Search for something")],
            null,
            CancellationToken.None);

        mockInner.Verify(x => x.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetResponseAsync_WithThinkTags_RetriesOnce()
    {
        var mockInner = new Mock<Microsoft.Extensions.AI.IChatClient>();
        var callCount = 0;

        mockInner.Setup(x => x.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Callback(() => callCount++)
            .Returns(() =>
            {
                var thinkResponse = "<think>reasoning...</think> Then the answer.";
                var response = callCount switch
                {
                    1 => new ChatResponse(new ChatMessage(ChatRole.Assistant, thinkResponse)),
                    _ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "Done"))
                };
                return Task.FromResult(response);
            });

        var client = new InterimTextRetryChatClient(mockInner.Object);

        var result = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Hello")],
            null,
            CancellationToken.None);

        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task GetResponseAsync_WithPlainText_NoRetry()
    {
        var mockInner = new Mock<Microsoft.Extensions.AI.IChatClient>();
        mockInner.Setup(x => x.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Final response")));

        var client = new InterimTextRetryChatClient(mockInner.Object);

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Hello")],
            null,
            CancellationToken.None);

        mockInner.Verify(x => x.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetResponseAsync_EmptyResponse_NoRetry()
    {
        var mockInner = new Mock<Microsoft.Extensions.AI.IChatClient>();
        mockInner.Setup(x => x.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "")));

        var client = new InterimTextRetryChatClient(mockInner.Object);
        var result = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Hello")],
            null,
            CancellationToken.None);

        mockInner.Verify(x => x.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Empty(result.Messages.FirstOrDefault()?.Text ?? "");
    }

    [Fact]
    public async Task GetStreamingResponseAsync_PassesThroughToInner()
    {
        var mockInner = new Mock<Microsoft.Extensions.AI.IChatClient>();
        var update = new ChatResponseUpdate(ChatRole.Assistant, "Streamed");
        mockInner.Setup(x => x.GetStreamingResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Returns(StreamOne(update));

        var client = new InterimTextRetryChatClient(mockInner.Object);
        var count = 0;
        await foreach (var u in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Hi")], null, CancellationToken.None))
        {
            count++;
            Assert.Equal("Streamed", u.Text);
        }
        Assert.Equal(1, count);
    }

    [Fact]
    public void GetService_DelegatesToInner()
    {
        var mockInner = new Mock<Microsoft.Extensions.AI.IChatClient>();
        var expected = new object();
        mockInner.Setup(x => x.GetService(typeof(string), "key")).Returns(expected);

        var client = new InterimTextRetryChatClient(mockInner.Object);
        var result = client.GetService(typeof(string), "key");

        Assert.Same(expected, result);
    }

    [Fact]
    public void Constructor_WithNullInner_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new InterimTextRetryChatClient(null!));
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> StreamOne(ChatResponseUpdate u)
    {
        yield return u;
        await Task.CompletedTask;
    }
}
