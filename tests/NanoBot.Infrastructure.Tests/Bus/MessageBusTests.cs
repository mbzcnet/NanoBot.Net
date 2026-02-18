using System.Threading.Channels;
using NanoBot.Core.Bus;
using NanoBot.Infrastructure.Bus;
using Xunit;

namespace NanoBot.Infrastructure.Tests.Bus;

public class MessageBusTests
{
    [Fact]
    public async Task PublishInboundAsync_ShouldIncreaseInboundSize()
    {
        using var bus = new MessageBus();
        var message = CreateInboundMessage("test", "user1", "chat1", "Hello");

        await bus.PublishInboundAsync(message);

        Assert.Equal(1, bus.InboundSize);
    }

    [Fact]
    public async Task ConsumeInboundAsync_ShouldReturnPublishedMessage()
    {
        using var bus = new MessageBus();
        var message = CreateInboundMessage("telegram", "user1", "chat1", "Test message");

        await bus.PublishInboundAsync(message);
        var consumed = await bus.ConsumeInboundAsync();

        Assert.Equal(message.Channel, consumed.Channel);
        Assert.Equal(message.SenderId, consumed.SenderId);
        Assert.Equal(message.ChatId, consumed.ChatId);
        Assert.Equal(message.Content, consumed.Content);
    }

    [Fact]
    public async Task PublishOutboundAsync_ShouldIncreaseOutboundSize()
    {
        using var bus = new MessageBus();
        var message = CreateOutboundMessage("telegram", "chat1", "Response");

        await bus.PublishOutboundAsync(message);

        Assert.Equal(1, bus.OutboundSize);
    }

    [Fact]
    public async Task ConsumeOutboundAsync_ShouldReturnPublishedMessage()
    {
        using var bus = new MessageBus();
        var message = CreateOutboundMessage("discord", "chat2", "Discord response");

        await bus.PublishOutboundAsync(message);
        var consumed = await bus.ConsumeOutboundAsync();

        Assert.Equal(message.Channel, consumed.Channel);
        Assert.Equal(message.ChatId, consumed.ChatId);
        Assert.Equal(message.Content, consumed.Content);
    }

    [Fact]
    public async Task InboundMessage_SessionKey_ShouldBeCorrectFormat()
    {
        var message = CreateInboundMessage("telegram", "user1", "chat123", "content");

        Assert.Equal("telegram:chat123", message.SessionKey);
    }

    [Fact]
    public async Task MessageBus_ShouldSupportMultipleProducers()
    {
        using var bus = new MessageBus();
        var tasks = Enumerable.Range(0, 10)
            .Select(i => bus.PublishInboundAsync(CreateInboundMessage("ch", $"u{i}", $"c{i}", $"msg{i}")).AsTask())
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(10, bus.InboundSize);
    }

    [Fact]
    public async Task MessageBus_ShouldSupportMultipleConsumers()
    {
        using var bus = new MessageBus();
        var consumed = new List<InboundMessage>();
        var cts = new CancellationTokenSource();

        for (int i = 0; i < 5; i++)
        {
            await bus.PublishInboundAsync(CreateInboundMessage("ch", $"u{i}", $"c{i}", $"msg{i}"));
        }

        var consumerTasks = Enumerable.Range(0, 5)
            .Select(_ => Task.Run(async () =>
            {
                var msg = await bus.ConsumeInboundAsync(cts.Token);
                lock (consumed) { consumed.Add(msg); }
            }))
            .ToArray();

        await Task.WhenAll(consumerTasks);

        Assert.Equal(5, consumed.Count);
        Assert.Equal(0, bus.InboundSize);
    }

    [Fact]
    public async Task SubscribeOutbound_ShouldRegisterCallback()
    {
        using var bus = new MessageBus();
        var receivedMessages = new List<OutboundMessage>();

        bus.SubscribeOutbound("telegram", msg =>
        {
            lock (receivedMessages) { receivedMessages.Add(msg); }
            return Task.CompletedTask;
        });

        await bus.PublishOutboundAsync(CreateOutboundMessage("telegram", "chat1", "Hello"));
        await bus.StartDispatcherAsync();

        await Task.Delay(100);

        Assert.Single(receivedMessages);
        Assert.Equal("Hello", receivedMessages[0].Content);
    }

    [Fact]
    public async Task Dispatcher_ShouldRouteToCorrectChannel()
    {
        using var bus = new MessageBus();
        var telegramMessages = new List<OutboundMessage>();
        var discordMessages = new List<OutboundMessage>();

        bus.SubscribeOutbound("telegram", msg =>
        {
            lock (telegramMessages) { telegramMessages.Add(msg); }
            return Task.CompletedTask;
        });
        bus.SubscribeOutbound("discord", msg =>
        {
            lock (discordMessages) { discordMessages.Add(msg); }
            return Task.CompletedTask;
        });

        await bus.PublishOutboundAsync(CreateOutboundMessage("telegram", "chat1", "To Telegram"));
        await bus.PublishOutboundAsync(CreateOutboundMessage("discord", "chat2", "To Discord"));

        await bus.StartDispatcherAsync();
        await Task.Delay(200);

        Assert.Single(telegramMessages);
        Assert.Single(discordMessages);
        Assert.Equal("To Telegram", telegramMessages[0].Content);
        Assert.Equal("To Discord", discordMessages[0].Content);
    }

    [Fact]
    public async Task Dispatcher_ShouldHandleNoSubscriber()
    {
        using var bus = new MessageBus();

        await bus.PublishOutboundAsync(CreateOutboundMessage("unknown", "chat1", "No subscriber"));
        await bus.StartDispatcherAsync();

        await Task.Delay(100);

        Assert.Equal(0, bus.OutboundSize);
    }

    [Fact]
    public async Task Stop_ShouldStopDispatcher()
    {
        using var bus = new MessageBus();
        var counter = 0;

        bus.SubscribeOutbound("test", msg =>
        {
            Interlocked.Increment(ref counter);
            return Task.CompletedTask;
        });

        await bus.StartDispatcherAsync();

        for (int i = 0; i < 5; i++)
        {
            await bus.PublishOutboundAsync(CreateOutboundMessage("test", "chat", $"msg{i}"));
        }

        await Task.Delay(100);
        bus.Stop();

        Assert.Equal(5, counter);
    }

    /// <summary>Testing.md: Stop_PreventsNewMessages - 停止后不能再发布出站消息（或新消息不会被分发）</summary>
    [Fact]
    public async Task Stop_PreventsNewMessagesFromBeingDispatched()
    {
        using var bus = new MessageBus();
        var receivedCount = 0;

        bus.SubscribeOutbound("test", msg =>
        {
            Interlocked.Increment(ref receivedCount);
            return Task.CompletedTask;
        });

        await bus.StartDispatcherAsync();
        await bus.PublishOutboundAsync(CreateOutboundMessage("test", "chat", "before-stop"));
        await Task.Delay(80);

        bus.Stop();

        await Task.Delay(50);
        Assert.Equal(1, receivedCount);

        await Assert.ThrowsAsync<ChannelClosedException>(async () =>
            await bus.PublishOutboundAsync(CreateOutboundMessage("test", "chat", "after-stop")));
    }

    [Fact]
    public async Task InboundSize_ReturnsCorrectCount()
    {
        using var bus = new MessageBus();
        Assert.Equal(0, bus.InboundSize);

        await bus.PublishInboundAsync(CreateInboundMessage("ch", "u1", "c1", "m1"));
        await bus.PublishInboundAsync(CreateInboundMessage("ch", "u2", "c2", "m2"));

        Assert.Equal(2, bus.InboundSize);
    }

    [Fact]
    public async Task ConsumeInboundAsync_ShouldThrowOperationCanceled_WhenCancelled()
    {
        using var bus = new MessageBus();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => bus.ConsumeInboundAsync(cts.Token).AsTask());
    }

    [Fact]
    public async Task Dispose_ShouldCleanupResources()
    {
        var bus = new MessageBus();
        await bus.PublishInboundAsync(CreateInboundMessage("test", "u1", "c1", "msg"));
        await bus.StartDispatcherAsync();

        bus.Dispose();

        // After disposal, the bus should be in a stopped state
        // Channel.Reader.Count may still work (Channel is not IDisposable)
        // but the bus should not accept new messages
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => bus.PublishInboundAsync(CreateInboundMessage("test", "u2", "c2", "msg")).AsTask());
    }

    [Fact]
    public async Task MessageBus_ShouldHandleHighThroughput()
    {
        using var bus = new MessageBus();
        const int messageCount = 100;

        var publishTasks = Enumerable.Range(0, messageCount)
            .Select(i => bus.PublishInboundAsync(CreateInboundMessage("ch", $"u{i}", $"c{i}", $"msg{i}")).AsTask())
            .ToArray();

        await Task.WhenAll(publishTasks);

        Assert.Equal(messageCount, bus.InboundSize);

        var consumeTasks = Enumerable.Range(0, messageCount)
            .Select(_ => bus.ConsumeInboundAsync().AsTask())
            .ToArray();

        await Task.WhenAll(consumeTasks);

        Assert.Equal(0, bus.InboundSize);
    }

    [Fact]
    public async Task OutboundMessage_ShouldSupportReplyTo()
    {
        using var bus = new MessageBus();
        var message = new OutboundMessage
        {
            Channel = "telegram",
            ChatId = "chat1",
            Content = "Reply content",
            ReplyTo = "original_msg_id"
        };

        await bus.PublishOutboundAsync(message);
        var consumed = await bus.ConsumeOutboundAsync();

        Assert.Equal("original_msg_id", consumed.ReplyTo);
    }

    [Fact]
    public async Task InboundMessage_ShouldSupportMedia()
    {
        using var bus = new MessageBus();
        var message = new InboundMessage
        {
            Channel = "telegram",
            SenderId = "user1",
            ChatId = "chat1",
            Content = "Check this image",
            Media = new List<string> { "/path/to/image.jpg", "/path/to/doc.pdf" }
        };

        await bus.PublishInboundAsync(message);
        var consumed = await bus.ConsumeInboundAsync();

        Assert.Equal(2, consumed.Media.Count);
        Assert.Contains("/path/to/image.jpg", consumed.Media);
    }

    [Fact]
    public async Task MessageBus_ShouldSupportMetadata()
    {
        using var bus = new MessageBus();
        var metadata = new Dictionary<string, object>
        {
            ["key1"] = "value1",
            ["key2"] = 123
        };
        var message = new InboundMessage
        {
            Channel = "test",
            SenderId = "user1",
            ChatId = "chat1",
            Content = "content",
            Metadata = metadata
        };

        await bus.PublishInboundAsync(message);
        var consumed = await bus.ConsumeInboundAsync();

        Assert.NotNull(consumed.Metadata);
        Assert.Equal("value1", consumed.Metadata!["key1"]);
        Assert.Equal(123, consumed.Metadata!["key2"]);
    }

    [Fact]
    public async Task Dispatcher_CallbackException_ShouldNotCrashDispatcher()
    {
        using var bus = new MessageBus();
        var successfulCalls = 0;

        bus.SubscribeOutbound("test", msg =>
        {
            if (msg.Content == "error")
                throw new InvalidOperationException("Test error");
            Interlocked.Increment(ref successfulCalls);
            return Task.CompletedTask;
        });

        await bus.PublishOutboundAsync(CreateOutboundMessage("test", "chat", "error"));
        await bus.PublishOutboundAsync(CreateOutboundMessage("test", "chat", "success"));

        await bus.StartDispatcherAsync();
        await Task.Delay(200);

        Assert.Equal(1, successfulCalls);
    }

    private static InboundMessage CreateInboundMessage(string channel, string senderId, string chatId, string content)
    {
        return new InboundMessage
        {
            Channel = channel,
            SenderId = senderId,
            ChatId = chatId,
            Content = content
        };
    }

    private static OutboundMessage CreateOutboundMessage(string channel, string chatId, string content)
    {
        return new OutboundMessage
        {
            Channel = channel,
            ChatId = chatId,
            Content = content
        };
    }
}
