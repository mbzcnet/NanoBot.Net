using Microsoft.Extensions.Logging;
using Moq;
using NanoBot.Channels.Implementations.Telegram;
using NanoBot.Channels.Implementations.Email;
using NanoBot.Channels.Implementations.Discord;
using NanoBot.Channels.Implementations.Slack;
using NanoBot.Channels.Implementations.Feishu;
using NanoBot.Channels.Implementations.DingTalk;
using NanoBot.Channels.Implementations.QQ;
using NanoBot.Channels.Implementations.Mochat;
using NanoBot.Channels.Implementations.WhatsApp;
using NanoBot.Core.Bus;
using NanoBot.Core.Configuration;
using Xunit;

namespace NanoBot.Channels.Tests;

public class TelegramChannelTests
{
    private readonly Mock<IMessageBus> _mockBus;
    private readonly Mock<ILogger<TelegramChannel>> _mockLogger;
    private readonly TelegramConfig _config;

    public TelegramChannelTests()
    {
        _mockBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<TelegramChannel>>();
        _config = new TelegramConfig();
    }

    [Fact]
    public void Constructor_InitializesProperties()
    {
        var channel = new TelegramChannel(_config, _mockBus.Object, _mockLogger.Object);

        Assert.Equal("telegram", channel.Id);
        Assert.Equal("telegram", channel.Type);
        Assert.False(channel.IsConnected);
    }

    [Fact]
    public async Task StartAsync_WithoutToken_DoesNotStart()
    {
        var channel = new TelegramChannel(_config, _mockBus.Object, _mockLogger.Object);

        var cts = new CancellationTokenSource();
        cts.CancelAfter(100);

        await channel.StartAsync(cts.Token);

        Assert.False(channel.IsConnected);
    }

    [Fact]
    public async Task StopAsync_StopsChannel()
    {
        var channel = new TelegramChannel(_config, _mockBus.Object, _mockLogger.Object);

        await channel.StopAsync();

        Assert.False(channel.IsConnected);
    }

    [Fact]
    public async Task SendMessageAsync_WithoutBotClient_LogsWarning()
    {
        var channel = new TelegramChannel(_config, _mockBus.Object, _mockLogger.Object);
        var message = new OutboundMessage
        {
            Channel = "telegram",
            ChatId = "123456",
            Content = "Hello"
        };

        await channel.SendMessageAsync(message);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("not running")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}

public class EmailChannelTests
{
    private readonly Mock<IMessageBus> _mockBus;
    private readonly Mock<ILogger<EmailChannel>> _mockLogger;
    private readonly EmailConfig _config;

    public EmailChannelTests()
    {
        _mockBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<EmailChannel>>();
        _config = new EmailConfig();
    }

    [Fact]
    public void Constructor_InitializesProperties()
    {
        var channel = new EmailChannel(_config, _mockBus.Object, _mockLogger.Object);

        Assert.Equal("email", channel.Id);
        Assert.Equal("email", channel.Type);
        Assert.False(channel.IsConnected);
    }

    [Fact]
    public async Task StartAsync_WithoutConsent_DoesNotStart()
    {
        _config.ConsentGranted = false;
        var channel = new EmailChannel(_config, _mockBus.Object, _mockLogger.Object);

        var cts = new CancellationTokenSource();
        cts.CancelAfter(100);

        await channel.StartAsync(cts.Token);

        Assert.False(channel.IsConnected);
    }

    [Fact]
    public async Task StartAsync_WithConsentButMissingConfig_LogsError()
    {
        _config.ConsentGranted = true;
        var channel = new EmailChannel(_config, _mockBus.Object, _mockLogger.Object);

        var cts = new CancellationTokenSource();
        cts.CancelAfter(100);

        await channel.StartAsync(cts.Token);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task StopAsync_StopsChannel()
    {
        var channel = new EmailChannel(_config, _mockBus.Object, _mockLogger.Object);

        await channel.StopAsync();

        Assert.False(channel.IsConnected);
    }

    [Fact]
    public async Task SendMessageAsync_WithoutConsent_Skips()
    {
        _config.ConsentGranted = false;
        var channel = new EmailChannel(_config, _mockBus.Object, _mockLogger.Object);
        var message = new OutboundMessage
        {
            Channel = "email",
            ChatId = "test@example.com",
            Content = "Hello"
        };

        await channel.SendMessageAsync(message);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("consent")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SendMessageAsync_WithConsentButNoAutoReply_Skips()
    {
        _config.ConsentGranted = true;
        _config.AutoReplyEnabled = false;
        var channel = new EmailChannel(_config, _mockBus.Object, _mockLogger.Object);
        var message = new OutboundMessage
        {
            Channel = "email",
            ChatId = "test@example.com",
            Content = "Hello"
        };

        await channel.SendMessageAsync(message);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("auto_reply")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}

public class DiscordChannelTests
{
    private readonly Mock<IMessageBus> _mockBus;
    private readonly Mock<ILogger<DiscordChannel>> _mockLogger;
    private readonly DiscordConfig _config;

    public DiscordChannelTests()
    {
        _mockBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<DiscordChannel>>();
        _config = new DiscordConfig();
    }

    [Fact]
    public void Constructor_InitializesProperties()
    {
        var channel = new DiscordChannel(_config, _mockBus.Object, _mockLogger.Object);

        Assert.Equal("discord", channel.Id);
        Assert.Equal("discord", channel.Type);
        Assert.False(channel.IsConnected);
    }

    [Fact]
    public async Task StartAsync_WithoutToken_DoesNotStart()
    {
        var channel = new DiscordChannel(_config, _mockBus.Object, _mockLogger.Object);

        var cts = new CancellationTokenSource();
        cts.CancelAfter(100);

        await channel.StartAsync(cts.Token);

        Assert.False(channel.IsConnected);
    }

    [Fact]
    public async Task StopAsync_StopsChannel()
    {
        var channel = new DiscordChannel(_config, _mockBus.Object, _mockLogger.Object);

        await channel.StopAsync();

        Assert.False(channel.IsConnected);
    }
}

public class SlackChannelTests
{
    private readonly Mock<IMessageBus> _mockBus;
    private readonly Mock<ILogger<SlackChannel>> _mockLogger;
    private readonly SlackConfig _config;

    public SlackChannelTests()
    {
        _mockBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<SlackChannel>>();
        _config = new SlackConfig();
    }

    [Fact]
    public void Constructor_InitializesProperties()
    {
        var channel = new SlackChannel(_config, _mockBus.Object, _mockLogger.Object);

        Assert.Equal("slack", channel.Id);
        Assert.Equal("slack", channel.Type);
        Assert.False(channel.IsConnected);
    }

    [Fact]
    public async Task StartAsync_WithoutToken_DoesNotStart()
    {
        var channel = new SlackChannel(_config, _mockBus.Object, _mockLogger.Object);

        var cts = new CancellationTokenSource();
        cts.CancelAfter(100);

        await channel.StartAsync(cts.Token);

        Assert.False(channel.IsConnected);
    }

    [Fact]
    public async Task StopAsync_StopsChannel()
    {
        var channel = new SlackChannel(_config, _mockBus.Object, _mockLogger.Object);

        await channel.StopAsync();

        Assert.False(channel.IsConnected);
    }
}

public class FeishuChannelTests
{
    private readonly Mock<IMessageBus> _mockBus;
    private readonly Mock<ILogger<FeishuChannel>> _mockLogger;
    private readonly FeishuConfig _config;

    public FeishuChannelTests()
    {
        _mockBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<FeishuChannel>>();
        _config = new FeishuConfig();
    }

    [Fact]
    public void Constructor_InitializesProperties()
    {
        var channel = new FeishuChannel(_config, _mockBus.Object, _mockLogger.Object);

        Assert.Equal("feishu", channel.Id);
        Assert.Equal("feishu", channel.Type);
        Assert.False(channel.IsConnected);
    }

    [Fact]
    public async Task StopAsync_StopsChannel()
    {
        var channel = new FeishuChannel(_config, _mockBus.Object, _mockLogger.Object);

        await channel.StopAsync();

        Assert.False(channel.IsConnected);
    }
}

public class DingTalkChannelTests
{
    private readonly Mock<IMessageBus> _mockBus;
    private readonly Mock<ILogger<DingTalkChannel>> _mockLogger;
    private readonly DingTalkConfig _config;

    public DingTalkChannelTests()
    {
        _mockBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<DingTalkChannel>>();
        _config = new DingTalkConfig();
    }

    [Fact]
    public void Constructor_InitializesProperties()
    {
        var channel = new DingTalkChannel(_config, _mockBus.Object, _mockLogger.Object);

        Assert.Equal("dingtalk", channel.Id);
        Assert.Equal("dingtalk", channel.Type);
        Assert.False(channel.IsConnected);
    }

    [Fact]
    public async Task StopAsync_StopsChannel()
    {
        var channel = new DingTalkChannel(_config, _mockBus.Object, _mockLogger.Object);

        await channel.StopAsync();

        Assert.False(channel.IsConnected);
    }
}

public class QQChannelTests
{
    private readonly Mock<IMessageBus> _mockBus;
    private readonly Mock<ILogger<QQChannel>> _mockLogger;
    private readonly QQConfig _config;

    public QQChannelTests()
    {
        _mockBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<QQChannel>>();
        _config = new QQConfig();
    }

    [Fact]
    public void Constructor_InitializesProperties()
    {
        var channel = new QQChannel(_config, _mockBus.Object, _mockLogger.Object);

        Assert.Equal("qq", channel.Id);
        Assert.Equal("qq", channel.Type);
        Assert.False(channel.IsConnected);
    }

    [Fact]
    public async Task StopAsync_StopsChannel()
    {
        var channel = new QQChannel(_config, _mockBus.Object, _mockLogger.Object);

        await channel.StopAsync();

        Assert.False(channel.IsConnected);
    }
}

public class MochatChannelTests
{
    private readonly Mock<IMessageBus> _mockBus;
    private readonly Mock<ILogger<MochatChannel>> _mockLogger;
    private readonly MochatConfig _config;

    public MochatChannelTests()
    {
        _mockBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<MochatChannel>>();
        _config = new MochatConfig();
    }

    [Fact]
    public void Constructor_InitializesProperties()
    {
        var channel = new MochatChannel(_config, _mockBus.Object, _mockLogger.Object);

        Assert.Equal("mochat", channel.Id);
        Assert.Equal("mochat", channel.Type);
        Assert.False(channel.IsConnected);
    }

    [Fact]
    public async Task StopAsync_StopsChannel()
    {
        var channel = new MochatChannel(_config, _mockBus.Object, _mockLogger.Object);

        await channel.StopAsync();

        Assert.False(channel.IsConnected);
    }
}

public class WhatsAppChannelTests
{
    private readonly Mock<IMessageBus> _mockBus;
    private readonly Mock<ILogger<WhatsAppChannel>> _mockLogger;
    private readonly WhatsAppConfig _config;

    public WhatsAppChannelTests()
    {
        _mockBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<WhatsAppChannel>>();
        _config = new WhatsAppConfig();
    }

    [Fact]
    public void Constructor_InitializesProperties()
    {
        var channel = new WhatsAppChannel(_config, _mockBus.Object, _mockLogger.Object);

        Assert.Equal("whatsapp", channel.Id);
        Assert.Equal("whatsapp", channel.Type);
        Assert.False(channel.IsConnected);
    }

    [Fact]
    public async Task StopAsync_StopsChannel()
    {
        var channel = new WhatsAppChannel(_config, _mockBus.Object, _mockLogger.Object);

        await channel.StopAsync();

        Assert.False(channel.IsConnected);
    }
}
