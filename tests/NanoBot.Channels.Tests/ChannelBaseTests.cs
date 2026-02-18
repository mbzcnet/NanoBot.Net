using Microsoft.Extensions.Logging;
using Moq;
using NanoBot.Channels.Abstractions;
using NanoBot.Core.Bus;
using Xunit;

namespace NanoBot.Channels.Tests;

public class TestableChannel : ChannelBase
{
    public TestableChannel(IMessageBus bus, ILogger logger) : base(bus, logger) { }

    public override string Id => "test";
    public override string Type => "test";

    public override Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public override Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public override Task SendMessageAsync(OutboundMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public bool TestIsAllowed(string senderId, IReadOnlyList<string> allowList) => IsAllowed(senderId, allowList);

    public async Task TestHandleMessageAsync(string senderId, string chatId, string content, IReadOnlyList<string>? media = null, IDictionary<string, object>? metadata = null)
        => await HandleMessageAsync(senderId, chatId, content, media, metadata);
}

public class ChannelBaseTests
{
    private readonly Mock<IMessageBus> _mockBus;
    private readonly Mock<ILogger> _mockLogger;

    public ChannelBaseTests()
    {
        _mockBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger>();
    }

    [Fact]
    public void IsAllowed_ReturnsTrueForEmptyAllowList()
    {
        var channel = new TestableChannel(_mockBus.Object, _mockLogger.Object);

        var result = channel.TestIsAllowed("user123", Array.Empty<string>());

        Assert.True(result);
    }

    [Fact]
    public void IsAllowed_ReturnsTrueForExactMatch()
    {
        var channel = new TestableChannel(_mockBus.Object, _mockLogger.Object);
        var allowList = new List<string> { "user123", "user456" };

        var result = channel.TestIsAllowed("user123", allowList);

        Assert.True(result);
    }

    [Fact]
    public void IsAllowed_ReturnsFalseForNotInList()
    {
        var channel = new TestableChannel(_mockBus.Object, _mockLogger.Object);
        var allowList = new List<string> { "user123", "user456" };

        var result = channel.TestIsAllowed("user789", allowList);

        Assert.False(result);
    }

    [Fact]
    public void IsAllowed_ReturnsTrueForPipeSeparatedMatch()
    {
        var channel = new TestableChannel(_mockBus.Object, _mockLogger.Object);
        var allowList = new List<string> { "username", "user456" };

        var result = channel.TestIsAllowed("12345|username", allowList);

        Assert.True(result);
    }

    [Fact]
    public async Task HandleMessageAsync_PublishesToBus()
    {
        var channel = new TestableChannel(_mockBus.Object, _mockLogger.Object);
        InboundMessage? publishedMessage = null;
        _mockBus.Setup(b => b.PublishInboundAsync(It.IsAny<InboundMessage>(), It.IsAny<CancellationToken>()))
            .Callback<InboundMessage, CancellationToken>((msg, _) => publishedMessage = msg)
            .Returns(ValueTask.CompletedTask);

        await channel.TestHandleMessageAsync("sender1", "chat1", "Hello");

        Assert.NotNull(publishedMessage);
        Assert.Equal("test", publishedMessage.Channel);
        Assert.Equal("sender1", publishedMessage.SenderId);
        Assert.Equal("chat1", publishedMessage.ChatId);
        Assert.Equal("Hello", publishedMessage.Content);
    }

    [Fact]
    public async Task HandleMessageAsync_RaisesMessageReceivedEvent()
    {
        var channel = new TestableChannel(_mockBus.Object, _mockLogger.Object);
        InboundMessage? receivedMessage = null;
        channel.MessageReceived += (sender, msg) => receivedMessage = msg;

        _mockBus.Setup(b => b.PublishInboundAsync(It.IsAny<InboundMessage>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        await channel.TestHandleMessageAsync("sender1", "chat1", "Hello");

        Assert.NotNull(receivedMessage);
        Assert.Equal("sender1", receivedMessage.SenderId);
    }
}
