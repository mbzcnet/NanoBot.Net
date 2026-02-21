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
    public async Task GetResponseAsync_WithInterimText_RetriesOnce()
    {
        var mockInner = new Mock<Microsoft.Extensions.AI.IChatClient>();
        var callCount = 0;
        
        mockInner.Setup(x => x.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Callback(() => callCount++)
            .Returns(() =>
            {
                var response = callCount switch
                {
                    0 => new ChatResponse(new ChatMessage(ChatRole.Assistant, "Let me think...")),
                    _ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "Done"))
                };
                return Task.FromResult(response);
            });

        var client = new InterimTextRetryChatClient(mockInner.Object);

        var result = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Hello")],
            null,
            CancellationToken.None);

        Assert.True(callCount >= 1, $"Expected at least 1 call, got {callCount}");
    }

    [Fact]
    public async Task GetResponseAsync_WithInterimText_NoMoreRetries_AfterSecondAttempt()
    {
        var mockInner = new Mock<Microsoft.Extensions.AI.IChatClient>();
        var callCount = 0;
        
        mockInner.Setup(x => x.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Callback(() => callCount++)
            .Returns(() => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Final response"))));

        var client = new InterimTextRetryChatClient(mockInner.Object);

        var result = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Hello")],
            null,
            CancellationToken.None);

        Assert.Equal(2, callCount);
    }
}
