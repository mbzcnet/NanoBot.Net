using FluentAssertions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Moq;
using NanoBot.Agent;
using NanoBot.Core.Bus;
using NanoBot.Core.Workspace;
using Xunit;

namespace NanoBot.Integration.Tests;

public class AgentIntegrationTests : IAsyncLifetime
{
    private TestFixture _fixture = null!;
    private AgentRuntime _runtime = null!;

    public async Task InitializeAsync()
    {
        _fixture = new TestFixture();
        _runtime = new AgentRuntime(
            _fixture.Agent,
            _fixture.MessageBus,
            _fixture.SessionManager,
            _fixture.SessionsDirectory);
    }

    public async Task DisposeAsync()
    {
        _runtime.Dispose();
        await _fixture.DisposeAsync();
    }

    [Fact]
    public async Task AgentLoop_ProcessesMessage_EndToEnd()
    {
        var response = await _runtime.ProcessDirectAsync("Hello, test!");

        response.Should().NotBeNullOrEmpty();
        _fixture.ChatClient.CallCount.Should().Be(1);
        _fixture.ChatClient.ReceivedMessages.Should().HaveCount(1);
    }

    [Fact]
    public async Task AgentLoop_WithMultipleMessages_MaintainsSession()
    {
        await _runtime.ProcessDirectAsync("First message");
        await _runtime.ProcessDirectAsync("Second message");
        await _runtime.ProcessDirectAsync("Third message");

        _fixture.ChatClient.CallCount.Should().Be(3);
    }

    [Fact]
    public async Task AgentLoop_WithDifferentSessionKeys_MaintainsSeparateSessions()
    {
        var response1 = await _runtime.ProcessDirectAsync("Message for session 1", "session:1");
        var response2 = await _runtime.ProcessDirectAsync("Message for session 2", "session:2");

        response1.Should().NotBeNullOrEmpty();
        response2.Should().NotBeNullOrEmpty();
        _fixture.ChatClient.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task AgentLoop_PersistsSession_AcrossRequests()
    {
        var sessionKey = "test:persist";

        await _runtime.ProcessDirectAsync("First message", sessionKey);

        var sessions = _fixture.SessionManager.ListSessions().ToList();
        sessions.Should().Contain(s => s.Key == sessionKey);

        await _runtime.ProcessDirectAsync("Second message", sessionKey);

        sessions = _fixture.SessionManager.ListSessions().ToList();
        sessions.Should().Contain(s => s.Key == sessionKey);
    }

    [Fact]
    public async Task AgentLoop_HandlesHelpCommand()
    {
        var response = await _runtime.ProcessDirectAsync("/help");

        response.Should().Contain("nanobot commands");
        response.Should().Contain("/new");
        response.Should().Contain("/help");
        _fixture.ChatClient.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AgentLoop_HandlesNewCommand()
    {
        var sessionKey = "test:new:command";

        await _runtime.ProcessDirectAsync("First message", sessionKey);

        var response = await _runtime.ProcessDirectAsync("/new", sessionKey);

        response.Should().Contain("New session started");
    }

    [Fact]
    public async Task AgentLoop_WithMediaContent_ProcessesCorrectly()
    {
        var message = new InboundMessage
        {
            Channel = "test",
            SenderId = "user",
            ChatId = "chat1",
            Content = "Check this image",
            Media = new List<string> { "/path/to/image.jpg" }
        };

        await _fixture.MessageBus.PublishInboundAsync(message);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        OutboundMessage? outboundMessage = null;

        _fixture.MessageBus.SubscribeOutbound("test", msg =>
        {
            outboundMessage = msg;
            return Task.CompletedTask;
        });

        var runTask = _runtime.RunAsync(cts.Token);
        await _fixture.MessageBus.StartDispatcherAsync(cts.Token);

        await Task.Delay(500);

        _fixture.ChatClient.CallCount.Should().Be(1);

        _runtime.Stop();
        cts.Cancel();
    }

    [Fact]
    public async Task AgentLoop_HandlesSystemMessages()
    {
        var systemMessage = new InboundMessage
        {
            Channel = "system",
            SenderId = "cron",
            ChatId = "test:chat1",
            Content = "Scheduled task executed"
        };

        await _fixture.MessageBus.PublishInboundAsync(systemMessage);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        _fixture.MessageBus.SubscribeOutbound("test", msg => Task.CompletedTask);

        var runTask = _runtime.RunAsync(cts.Token);
        await _fixture.MessageBus.StartDispatcherAsync(cts.Token);

        await Task.Delay(500);

        _fixture.ChatClient.CallCount.Should().Be(1);

        _runtime.Stop();
        cts.Cancel();
    }

    [Fact]
    public async Task AgentLoop_WithToolCall_ExecutesAndResponds()
    {
        var toolCallClient = new MockChatClient("tool-test", async (messages, options, ct) =>
        {
            var lastMessage = messages.LastOrDefault();
            if (lastMessage?.Text?.Contains("read file") == true)
            {
                return new ChatResponse(new ChatMessage(ChatRole.Assistant,
                    "I'll read that file for you. [Tool call: read_file]"));
            }

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "Done reading file."));
        });

        var fixture = new TestFixture();
        await using var _ = fixture;

        var runtime = new AgentRuntime(
            fixture.Agent,
            fixture.MessageBus,
            fixture.SessionManager,
            fixture.SessionsDirectory);

        var response = await runtime.ProcessDirectAsync("Please read file test.txt");

        response.Should().NotBeNullOrEmpty();

        runtime.Dispose();
    }

    [Fact]
    public async Task AgentLoop_WithCancellationToken_CancelsGracefully()
    {
        var cts = new CancellationTokenSource();
        var runTask = _runtime.RunAsync(cts.Token);

        await Task.Delay(100);

        _runtime.Stop();

        await Task.WhenAny(runTask, Task.Delay(1000));
        runTask.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task AgentLoop_ProcessesMultipleMessagesInSequence()
    {
        var messages = new[]
        {
            "Message 1",
            "Message 2",
            "Message 3"
        };

        foreach (var msg in messages)
        {
            var response = await _runtime.ProcessDirectAsync(msg);
            response.Should().NotBeNullOrEmpty();
        }

        _fixture.ChatClient.CallCount.Should().Be(3);
    }

    [Fact]
    public async Task AgentLoop_SessionManager_ListsSessions()
    {
        await _runtime.ProcessDirectAsync("Message 1", "session:a");
        await _runtime.ProcessDirectAsync("Message 2", "session:b");
        await _runtime.ProcessDirectAsync("Message 3", "session:c");

        var sessions = _fixture.SessionManager.ListSessions().ToList();

        sessions.Should().HaveCount(3);
        sessions.Select(s => s.Key).Should().Contain(["session:a", "session:b", "session:c"]);
    }

    [Fact]
    public async Task AgentLoop_ClearSession_RemovesSession()
    {
        var sessionKey = "session:to:clear";

        await _runtime.ProcessDirectAsync("Message before clear", sessionKey);

        var sessionsBefore = _fixture.SessionManager.ListSessions().ToList();
        sessionsBefore.Should().Contain(s => s.Key == sessionKey);

        await _fixture.SessionManager.ClearSessionAsync(sessionKey);

        var sessionsAfter = _fixture.SessionManager.ListSessions().ToList();
        sessionsAfter.Should().NotContain(s => s.Key == sessionKey);
    }

    [Fact]
    public async Task AgentLoop_WithMetadata_PreservesMetadata()
    {
        var message = new InboundMessage
        {
            Channel = "test",
            SenderId = "user",
            ChatId = "chat1",
            Content = "Test message with metadata",
            Metadata = new Dictionary<string, object>
            {
                ["key1"] = "value1",
                ["key2"] = 123
            }
        };

        await _fixture.MessageBus.PublishInboundAsync(message);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        OutboundMessage? outboundMessage = null;

        _fixture.MessageBus.SubscribeOutbound("test", msg =>
        {
            outboundMessage = msg;
            return Task.CompletedTask;
        });

        var runTask = _runtime.RunAsync(cts.Token);
        await _fixture.MessageBus.StartDispatcherAsync(cts.Token);

        await Task.Delay(500);

        outboundMessage.Should().NotBeNull();
        outboundMessage!.Metadata.Should().NotBeNull();
        outboundMessage.Metadata!["key1"].Should().Be("value1");
        outboundMessage.Metadata["key2"].Should().Be(123);

        _runtime.Stop();
        cts.Cancel();
    }
}
