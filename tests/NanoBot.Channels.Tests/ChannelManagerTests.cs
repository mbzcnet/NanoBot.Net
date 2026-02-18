using Microsoft.Extensions.Logging;
using Moq;
using NanoBot.Core.Bus;
using NanoBot.Core.Channels;
using NanoBot.Channels;
using Xunit;

namespace NanoBot.Channels.Tests;

public class ChannelManagerTests
{
    private readonly Mock<IMessageBus> _mockBus;
    private readonly Mock<ILogger<ChannelManager>> _mockLogger;

    public ChannelManagerTests()
    {
        _mockBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<ChannelManager>>();
    }

    [Fact]
    public void Constructor_InitializesEmpty()
    {
        var manager = new ChannelManager(_mockBus.Object, _mockLogger.Object);

        Assert.Empty(manager.EnabledChannels);
        Assert.Empty(manager.GetAllChannels());
    }

    [Fact]
    public void Register_AddsChannel()
    {
        var manager = new ChannelManager(_mockBus.Object, _mockLogger.Object);
        var mockChannel = new Mock<IChannel>();
        mockChannel.Setup(c => c.Id).Returns("test");
        mockChannel.Setup(c => c.Type).Returns("test");

        manager.Register(mockChannel.Object);

        Assert.Single(manager.EnabledChannels);
        Assert.Equal("test", manager.EnabledChannels[0]);
    }

    [Fact]
    public void GetChannel_ReturnsRegisteredChannel()
    {
        var manager = new ChannelManager(_mockBus.Object, _mockLogger.Object);
        var mockChannel = new Mock<IChannel>();
        mockChannel.Setup(c => c.Id).Returns("test");
        mockChannel.Setup(c => c.Type).Returns("test");

        manager.Register(mockChannel.Object);

        var result = manager.GetChannel("test");

        Assert.NotNull(result);
        Assert.Equal("test", result.Id);
    }

    [Fact]
    public void GetChannel_ReturnsNullForUnknown()
    {
        var manager = new ChannelManager(_mockBus.Object, _mockLogger.Object);

        var result = manager.GetChannel("unknown");

        Assert.Null(result);
    }

    [Fact]
    public void GetChannelsByType_ReturnsMatchingChannels()
    {
        var manager = new ChannelManager(_mockBus.Object, _mockLogger.Object);
        var mockChannel1 = new Mock<IChannel>();
        mockChannel1.Setup(c => c.Id).Returns("test1");
        mockChannel1.Setup(c => c.Type).Returns("telegram");
        var mockChannel2 = new Mock<IChannel>();
        mockChannel2.Setup(c => c.Id).Returns("test2");
        mockChannel2.Setup(c => c.Type).Returns("discord");

        manager.Register(mockChannel1.Object);
        manager.Register(mockChannel2.Object);

        var result = manager.GetChannelsByType("telegram");

        Assert.Single(result);
        Assert.Equal("test1", result[0].Id);
    }

    [Fact]
    public void GetStatus_ReturnsCorrectStatus()
    {
        var manager = new ChannelManager(_mockBus.Object, _mockLogger.Object);
        var mockChannel = new Mock<IChannel>();
        mockChannel.Setup(c => c.Id).Returns("test");
        mockChannel.Setup(c => c.Type).Returns("test");
        mockChannel.Setup(c => c.IsConnected).Returns(true);

        manager.Register(mockChannel.Object);

        var status = manager.GetStatus();

        Assert.True(status["test"].Enabled);
        Assert.True(status["test"].Running);
    }

    [Fact]
    public async Task StopAllAsync_StopsAllChannels()
    {
        var manager = new ChannelManager(_mockBus.Object, _mockLogger.Object);
        var mockChannel = new Mock<IChannel>();
        mockChannel.Setup(c => c.Id).Returns("test");
        mockChannel.Setup(c => c.Type).Returns("test");

        manager.Register(mockChannel.Object);

        await manager.StopAllAsync();

        mockChannel.Verify(c => c.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
