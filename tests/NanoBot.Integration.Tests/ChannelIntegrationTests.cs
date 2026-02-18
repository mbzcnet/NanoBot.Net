using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NanoBot.Channels;
using NanoBot.Core.Bus;
using NanoBot.Core.Channels;
using NanoBot.Infrastructure.Bus;
using Xunit;

namespace NanoBot.Integration.Tests;

public class ChannelIntegrationTests : IAsyncDisposable
{
    private readonly IMessageBus _messageBus;
    private readonly Mock<ILogger<ChannelManager>> _loggerMock;
    private readonly string _testDirectory;

    public ChannelIntegrationTests()
    {
        _messageBus = new MessageBus();
        _loggerMock = new Mock<ILogger<ChannelManager>>();
        _testDirectory = Path.Combine(Path.GetTempPath(), $"nanobot_channel_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    public async ValueTask DisposeAsync()
    {
        (_messageBus as IDisposable)?.Dispose();

        var retries = 0;
        while (retries < 5)
        {
            try
            {
                if (Directory.Exists(_testDirectory))
                {
                    Directory.Delete(_testDirectory, recursive: true);
                }
                break;
            }
            catch (IOException)
            {
                await Task.Delay(100);
                retries++;
            }
        }
    }

    [Fact]
    public async Task ChannelManager_StartAllAsync_StartsAllChannels()
    {
        var manager = new ChannelManager(_messageBus, _loggerMock.Object);
        var mockChannel1 = CreateMockChannel("channel1", "telegram");
        var mockChannel2 = CreateMockChannel("channel2", "discord");

        manager.Register(mockChannel1.Object);
        manager.Register(mockChannel2.Object);

        await manager.StartAllAsync();

        mockChannel1.Verify(c => c.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
        mockChannel2.Verify(c => c.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChannelManager_StopAllAsync_StopsAllChannels()
    {
        var manager = new ChannelManager(_messageBus, _loggerMock.Object);
        var mockChannel = CreateMockChannel("test", "test");
        manager.Register(mockChannel.Object);

        await manager.StopAllAsync();

        mockChannel.Verify(c => c.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChannelManager_MessageReceived_RoutesToBus()
    {
        var manager = new ChannelManager(_messageBus, _loggerMock.Object);
        var mockChannel = CreateMockChannel("test", "telegram");
        InboundMessage? receivedMessage = null;

        mockChannel.Setup(c => c.StartAsync(It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                mockChannel.Raise(c => c.MessageReceived += null,
                    mockChannel.Object,
                    new InboundMessage
                    {
                        Channel = "telegram",
                        SenderId = "user1",
                        ChatId = "chat1",
                        Content = "Test message"
                    });
            });

        manager.Register(mockChannel.Object);
        manager.MessageReceived += (sender, msg) => receivedMessage = msg;

        await manager.StartAllAsync();
        await Task.Delay(100);

        receivedMessage.Should().NotBeNull();
        receivedMessage!.Channel.Should().Be("telegram");
        receivedMessage.Content.Should().Be("Test message");
    }

    [Fact]
    public async Task ChannelManager_DispatchesOutboundMessages()
    {
        var manager = new ChannelManager(_messageBus, _loggerMock.Object);
        var mockChannel = CreateMockChannel("telegram", "telegram");
        mockChannel.Setup(c => c.SendMessageAsync(It.IsAny<OutboundMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        manager.Register(mockChannel.Object);

        var outboundMessage = new OutboundMessage
        {
            Channel = "telegram",
            ChatId = "chat1",
            Content = "Response message"
        };

        await _messageBus.PublishOutboundAsync(outboundMessage);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var startTask = manager.StartAllAsync(cts.Token);

        await Task.Delay(500);

        mockChannel.Verify(c => c.SendMessageAsync(
            It.Is<OutboundMessage>(m => m.Content == "Response message"),
            It.IsAny<CancellationToken>()), Times.Once);

        await manager.StopAllAsync();
    }

    [Fact]
    public async Task ChannelManager_GetStatus_ReturnsCorrectStatus()
    {
        var manager = new ChannelManager(_messageBus, _loggerMock.Object);
        var mockChannel1 = CreateMockChannel("telegram", "telegram", isConnected: true);
        var mockChannel2 = CreateMockChannel("discord", "discord", isConnected: false);

        manager.Register(mockChannel1.Object);
        manager.Register(mockChannel2.Object);

        var status = manager.GetStatus();

        status.Should().HaveCount(2);
        status["telegram"].Enabled.Should().BeTrue();
        status["telegram"].Running.Should().BeTrue();
        status["discord"].Enabled.Should().BeTrue();
        status["discord"].Running.Should().BeFalse();
    }

    [Fact]
    public async Task ChannelManager_GetChannelsByType_ReturnsCorrectChannels()
    {
        var manager = new ChannelManager(_messageBus, _loggerMock.Object);
        var telegramChannel1 = CreateMockChannel("telegram1", "telegram");
        var telegramChannel2 = CreateMockChannel("telegram2", "telegram");
        var discordChannel = CreateMockChannel("discord1", "discord");

        manager.Register(telegramChannel1.Object);
        manager.Register(telegramChannel2.Object);
        manager.Register(discordChannel.Object);

        var telegramChannels = manager.GetChannelsByType("telegram");

        telegramChannels.Should().HaveCount(2);
        telegramChannels.All(c => c.Type == "telegram").Should().BeTrue();
    }

    [Fact]
    public async Task MessageBus_HighThroughput_MultipleChannels()
    {
        var receivedMessages = new List<OutboundMessage>();
        var channels = new[] { "telegram", "discord", "slack" };

        foreach (var channel in channels)
        {
            _messageBus.SubscribeOutbound(channel, msg =>
            {
                lock (receivedMessages)
                {
                    receivedMessages.Add(msg);
                }
                return Task.CompletedTask;
            });
        }

        await _messageBus.StartDispatcherAsync();

        var publishTasks = Enumerable.Range(0, 30)
            .Select(i => _messageBus.PublishOutboundAsync(new OutboundMessage
            {
                Channel = channels[i % 3],
                ChatId = $"chat{i}",
                Content = $"Message {i}"
            }).AsTask())
            .ToArray();

        await Task.WhenAll(publishTasks);
        await Task.Delay(500);

        receivedMessages.Should().HaveCount(30);
    }

    [Fact]
    public async Task ChannelManager_HandlesChannelException_Gracefully()
    {
        var manager = new ChannelManager(_messageBus, _loggerMock.Object);
        var mockChannel = CreateMockChannel("error", "test");

        mockChannel.Setup(c => c.StartAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Channel failed to start"));

        manager.Register(mockChannel.Object);

        var act = async () => await manager.StartAllAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ChannelManager_HandlesSendException_Gracefully()
    {
        var manager = new ChannelManager(_messageBus, _loggerMock.Object);
        var mockChannel = CreateMockChannel("error", "test");

        mockChannel.Setup(c => c.SendMessageAsync(It.IsAny<OutboundMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Send failed"));

        manager.Register(mockChannel.Object);

        await _messageBus.PublishOutboundAsync(new OutboundMessage
        {
            Channel = "error",
            ChatId = "chat1",
            Content = "Test"
        });

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await manager.StartAllAsync(cts.Token);
        await Task.Delay(500);

        mockChannel.Verify(c => c.SendMessageAsync(
            It.IsAny<OutboundMessage>(),
            It.IsAny<CancellationToken>()), Times.Once);

        await manager.StopAllAsync();
    }

    [Fact]
    public async Task ChannelManager_Register_DoesNotAddDuplicate()
    {
        var manager = new ChannelManager(_messageBus, _loggerMock.Object);
        var mockChannel = CreateMockChannel("duplicate", "test");

        manager.Register(mockChannel.Object);
        manager.Register(mockChannel.Object);

        manager.EnabledChannels.Should().HaveCount(1);
    }

    [Fact]
    public async Task ChannelManager_UnknownChannel_DoesNotCrash()
    {
        var manager = new ChannelManager(_messageBus, _loggerMock.Object);

        await _messageBus.PublishOutboundAsync(new OutboundMessage
        {
            Channel = "unknown",
            ChatId = "chat1",
            Content = "Test"
        });

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await manager.StartAllAsync(cts.Token);
        await Task.Delay(300);

        await manager.StopAllAsync();
    }

    private static Mock<IChannel> CreateMockChannel(string id, string type, bool isConnected = true)
    {
        var mock = new Mock<IChannel>();
        mock.Setup(c => c.Id).Returns(id);
        mock.Setup(c => c.Type).Returns(type);
        mock.Setup(c => c.IsConnected).Returns(isConnected);
        mock.Setup(c => c.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mock.Setup(c => c.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mock.Setup(c => c.SendMessageAsync(It.IsAny<OutboundMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }
}
