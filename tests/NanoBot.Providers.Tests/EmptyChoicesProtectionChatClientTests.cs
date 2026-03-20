using Microsoft.Extensions.AI;
using Moq;
using NanoBot.Providers.Decorators;
using Xunit;

namespace NanoBot.Providers.Tests;

public class EmptyChoicesProtectionChatClientTests
{
    [Fact]
    public async Task GetResponseAsync_NormalResponse_PassesThrough()
    {
        var mockInner = new Mock<IChatClient>();
        var expectedResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello"));
        mockInner.Setup(x => x.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var client = new EmptyChoicesProtectionChatClient(mockInner.Object);

        var result = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Hi")],
            null,
            CancellationToken.None);

        Assert.Same(expectedResponse, result);
        mockInner.Verify(x => x.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetResponseAsync_EmptyMessages_ReturnsPlaceholder()
    {
        var mockInner = new Mock<IChatClient>();
        var emptyResponse = new ChatResponse(); // Empty messages
        mockInner.Setup(x => x.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyResponse);

        var client = new EmptyChoicesProtectionChatClient(mockInner.Object);

        var result = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Hi")],
            null,
            CancellationToken.None);

        Assert.Single(result.Messages);
        Assert.Equal(ChatRole.Assistant, result.Messages.First().Role);
        Assert.Equal(string.Empty, result.Messages.First().Text);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_PassesThrough()
    {
        var mockInner = new Mock<IChatClient>();
        var update = new ChatResponseUpdate(ChatRole.Assistant, "Streamed");
        mockInner.Setup(x => x.GetStreamingResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Returns(StreamOne(update));

        var client = new EmptyChoicesProtectionChatClient(mockInner.Object);
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
        var mockInner = new Mock<IChatClient>();
        var expected = new object();
        mockInner.Setup(x => x.GetService(typeof(string), "key")).Returns(expected);

        var client = new EmptyChoicesProtectionChatClient(mockInner.Object);
        var result = client.GetService(typeof(string), "key");

        Assert.Same(expected, result);
    }

    [Fact]
    public void Constructor_WithNullInner_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new EmptyChoicesProtectionChatClient(null!));
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> StreamOne(ChatResponseUpdate u)
    {
        yield return u;
        await Task.CompletedTask;
    }
}
