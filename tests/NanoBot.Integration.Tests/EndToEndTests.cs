using FluentAssertions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using NanoBot.Agent;
using NanoBot.Channels;
using NanoBot.Core.Bus;
using NanoBot.Core.Channels;
using NanoBot.Core.Skills;
using NanoBot.Core.Workspace;
using NanoBot.Infrastructure.Bus;
using Xunit;

namespace NanoBot.Integration.Tests;

public class EndToEndTests : IAsyncLifetime
{
    private IMessageBus _messageBus = null!;
    private MockChatClient _chatClient = null!;
    private ChatClientAgent _agent = null!;
    private ISessionManager _sessionManager = null!;
    private ChannelManager _channelManager = null!;
    private AgentRuntime _runtime = null!;
    private string _testDirectory = null!;
    private string _sessionsDirectory = null!;
    private Mock<IWorkspaceManager> _workspaceMock = null!;

    public async Task InitializeAsync()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"nanobot_e2e_{Guid.NewGuid():N}");
        _sessionsDirectory = Path.Combine(_testDirectory, "sessions");
        Directory.CreateDirectory(_testDirectory);
        Directory.CreateDirectory(_sessionsDirectory);
        Directory.CreateDirectory(Path.Combine(_testDirectory, "memory"));
        Directory.CreateDirectory(Path.Combine(_testDirectory, "skills"));

        File.WriteAllText(Path.Combine(_testDirectory, "AGENTS.md"), "# Test Agent");
        File.WriteAllText(Path.Combine(_testDirectory, "SOUL.md"), "# Test Soul");

        _messageBus = new MessageBus();
        _chatClient = new MockChatClient("e2e-provider");

        _workspaceMock = CreateWorkspaceMockForDir(_testDirectory, _sessionsDirectory);
        var skillsLoaderMock = CreateSkillsLoaderMock();
        var loggerFactory = LoggerFactory.Create(builder => { });

        _agent = NanoBotAgentFactory.Create(
            _chatClient,
            _workspaceMock.Object,
            skillsLoaderMock.Object,
            tools: null,
            loggerFactory: loggerFactory);

        _sessionManager = new SessionManager(_agent, _workspaceMock.Object);

        var channelLoggerMock = new Mock<ILogger<ChannelManager>>();
        _channelManager = new ChannelManager(_messageBus, channelLoggerMock.Object);

        _runtime = new AgentRuntime(
            _agent,
            _messageBus,
            _sessionManager,
            _sessionsDirectory,
            loggerFactory.CreateLogger<AgentRuntime>());
    }

    public async Task DisposeAsync()
    {
        _runtime.Dispose();
        _channelManager.Dispose();
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
    public async Task EndToEnd_MessageFromChannel_ProcessesAndResponds()
    {
        var mockChannel = CreateMockChannel("telegram", "telegram");
        OutboundMessage? sentMessage = null;

        mockChannel.Setup(c => c.SendMessageAsync(It.IsAny<OutboundMessage>(), It.IsAny<CancellationToken>()))
            .Callback<OutboundMessage, CancellationToken>((msg, _) => sentMessage = msg)
            .Returns(Task.CompletedTask);

        _channelManager.Register(mockChannel.Object);
        _channelManager.MessageReceived += async (sender, msg) =>
        {
            await _messageBus.PublishInboundAsync(msg);
        };

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var runtimeTask = _runtime.RunAsync(cts.Token);
        await _channelManager.StartAllAsync(cts.Token);
        await _messageBus.StartDispatcherAsync(cts.Token);

        mockChannel.Raise(c => c.MessageReceived += null,
            mockChannel.Object,
            new InboundMessage
            {
                Channel = "telegram",
                SenderId = "user123",
                ChatId = "chat456",
                Content = "Hello from Telegram!"
            });

        await Task.Delay(1500);

        _chatClient.CallCount.Should().Be(1);
        sentMessage.Should().NotBeNull();
        sentMessage!.Channel.Should().Be("telegram");
        sentMessage.ChatId.Should().Be("chat456");

        _runtime.Stop();
        cts.Cancel();
    }

    [Fact]
    public async Task EndToEnd_MultipleChannels_ProcessIndependently()
    {
        var telegramChannel = CreateMockChannel("telegram", "telegram");
        var discordChannel = CreateMockChannel("discord", "discord");

        var telegramMessages = new List<OutboundMessage>();
        var discordMessages = new List<OutboundMessage>();

        telegramChannel.Setup(c => c.SendMessageAsync(It.IsAny<OutboundMessage>(), It.IsAny<CancellationToken>()))
            .Callback<OutboundMessage, CancellationToken>((msg, _) => telegramMessages.Add(msg))
            .Returns(Task.CompletedTask);

        discordChannel.Setup(c => c.SendMessageAsync(It.IsAny<OutboundMessage>(), It.IsAny<CancellationToken>()))
            .Callback<OutboundMessage, CancellationToken>((msg, _) => discordMessages.Add(msg))
            .Returns(Task.CompletedTask);

        _channelManager.Register(telegramChannel.Object);
        _channelManager.Register(discordChannel.Object);
        _channelManager.MessageReceived += async (sender, msg) =>
        {
            await _messageBus.PublishInboundAsync(msg);
        };

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var runtimeTask = _runtime.RunAsync(cts.Token);
        await _channelManager.StartAllAsync(cts.Token);
        await _messageBus.StartDispatcherAsync(cts.Token);

        telegramChannel.Raise(c => c.MessageReceived += null,
            telegramChannel.Object,
            new InboundMessage { Channel = "telegram", SenderId = "u1", ChatId = "c1", Content = "Telegram msg" });

        discordChannel.Raise(c => c.MessageReceived += null,
            discordChannel.Object,
            new InboundMessage { Channel = "discord", SenderId = "u2", ChatId = "c2", Content = "Discord msg" });

        await Task.Delay(1500);

        telegramMessages.Should().HaveCount(1);
        telegramMessages[0].Channel.Should().Be("telegram");

        discordMessages.Should().HaveCount(1);
        discordMessages[0].Channel.Should().Be("discord");

        _chatClient.CallCount.Should().Be(2);

        _runtime.Stop();
        cts.Cancel();
    }

    [Fact]
    public async Task EndToEnd_SessionPersistence_AcrossRestarts()
    {
        var sessionKey = "testsession123";

        await _runtime.ProcessDirectAsync("First message", sessionKey);

        var sessions = _sessionManager.ListSessions().ToList();
        sessions.Should().Contain(s => s.Key == sessionKey);

        var sessionFiles = Directory.GetFiles(_sessionsDirectory, "*.json");
        sessionFiles.Should().HaveCount(1);

        _runtime.Dispose();
    }

    [Fact]
    public async Task EndToEnd_HelpCommand_DoesNotCallLLM()
    {
        var response = await _runtime.ProcessDirectAsync("/help");

        response.Should().Contain("nanobot commands");
        _chatClient.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task EndToEnd_NewCommand_ClearsSession()
    {
        var sessionKey = "test:clear";

        await _runtime.ProcessDirectAsync("Message before clear", sessionKey);

        var sessionsBefore = _sessionManager.ListSessions().ToList();
        sessionsBefore.Should().Contain(s => s.Key == sessionKey);

        var response = await _runtime.ProcessDirectAsync("/new", sessionKey);

        response.Should().Contain("New session started");
    }

    [Fact]
    public async Task EndToEnd_SystemMessage_ProcessesCorrectly()
    {
        var mockChannel = CreateMockChannel("telegram", "telegram");
        OutboundMessage? sentMessage = null;

        mockChannel.Setup(c => c.SendMessageAsync(It.IsAny<OutboundMessage>(), It.IsAny<CancellationToken>()))
            .Callback<OutboundMessage, CancellationToken>((msg, _) => sentMessage = msg)
            .Returns(Task.CompletedTask);

        _channelManager.Register(mockChannel.Object);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var runtimeTask = _runtime.RunAsync(cts.Token);
        await _channelManager.StartAllAsync(cts.Token);
        await _messageBus.StartDispatcherAsync(cts.Token);

        var systemMessage = new InboundMessage
        {
            Channel = "system",
            SenderId = "scheduler",
            ChatId = "telegram:scheduled-chat",
            Content = "Scheduled task: Daily reminder"
        };

        await _messageBus.PublishInboundAsync(systemMessage);

        await Task.Delay(1000);

        _chatClient.CallCount.Should().Be(1);
        sentMessage.Should().NotBeNull();
        sentMessage!.Channel.Should().Be("telegram");
        sentMessage.ChatId.Should().Be("scheduled-chat");

        _runtime.Stop();
        cts.Cancel();
    }

    [Fact]
    public async Task EndToEnd_GracefulShutdown()
    {
        var cts = new CancellationTokenSource();
        var runtimeTask = _runtime.RunAsync(cts.Token);

        await Task.Delay(100);

        _runtime.Stop();

        await Task.WhenAny(runtimeTask, Task.Delay(2000));
        runtimeTask.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task EndToEnd_MultipleMessagesInSequence()
    {
        var messages = new[]
        {
            "What is the weather?",
            "Tell me a joke",
            "What time is it?"
        };

        foreach (var msg in messages)
        {
            var response = await _runtime.ProcessDirectAsync(msg);
            response.Should().NotBeNullOrEmpty();
        }

        _chatClient.CallCount.Should().Be(3);
    }

    private static Mock<IWorkspaceManager> CreateWorkspaceMock()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"nanobot_mock_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);
        Directory.CreateDirectory(Path.Combine(testDir, "memory"));
        Directory.CreateDirectory(Path.Combine(testDir, "skills"));

        File.WriteAllText(Path.Combine(testDir, "AGENTS.md"), "# Test Agent");
        File.WriteAllText(Path.Combine(testDir, "SOUL.md"), "# Test Soul");

        var mock = new Mock<IWorkspaceManager>();
        mock.Setup(w => w.GetWorkspacePath()).Returns(testDir);
        mock.Setup(w => w.GetSessionsPath()).Returns(Path.Combine(testDir, "sessions"));
        mock.Setup(w => w.GetAgentsFile()).Returns(Path.Combine(testDir, "AGENTS.md"));
        mock.Setup(w => w.GetSoulFile()).Returns(Path.Combine(testDir, "SOUL.md"));
        mock.Setup(w => w.GetMemoryFile()).Returns(Path.Combine(testDir, "memory", "MEMORY.md"));
        mock.Setup(w => w.GetHistoryFile()).Returns(Path.Combine(testDir, "memory", "HISTORY.md"));
        mock.Setup(w => w.GetSkillsPath()).Returns(Path.Combine(testDir, "skills"));
        mock.Setup(w => w.GetMemoryPath()).Returns(Path.Combine(testDir, "memory"));
        mock.Setup(w => w.GetToolsFile()).Returns(Path.Combine(testDir, "TOOLS.md"));
        mock.Setup(w => w.GetUserFile()).Returns(Path.Combine(testDir, "USER.md"));
        mock.Setup(w => w.GetHeartbeatFile()).Returns(Path.Combine(testDir, "HEARTBEAT.md"));
        mock.Setup(w => w.FileExists(It.IsAny<string>())).Returns(false);
        mock.Setup(w => w.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        mock.Setup(w => w.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(w => w.AppendFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(w => w.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(w => w.EnsureDirectory(It.IsAny<string>()));
        return mock;
    }

    private static Mock<IWorkspaceManager> CreateWorkspaceMockForDir(string testDir, string sessionsDir)
    {
        var mock = new Mock<IWorkspaceManager>();
        mock.Setup(w => w.GetWorkspacePath()).Returns(testDir);
        mock.Setup(w => w.GetSessionsPath()).Returns(sessionsDir);
        mock.Setup(w => w.GetAgentsFile()).Returns(Path.Combine(testDir, "AGENTS.md"));
        mock.Setup(w => w.GetSoulFile()).Returns(Path.Combine(testDir, "SOUL.md"));
        mock.Setup(w => w.GetMemoryFile()).Returns(Path.Combine(testDir, "memory", "MEMORY.md"));
        mock.Setup(w => w.GetHistoryFile()).Returns(Path.Combine(testDir, "memory", "HISTORY.md"));
        mock.Setup(w => w.GetSkillsPath()).Returns(Path.Combine(testDir, "skills"));
        mock.Setup(w => w.GetMemoryPath()).Returns(Path.Combine(testDir, "memory"));
        mock.Setup(w => w.GetToolsFile()).Returns(Path.Combine(testDir, "TOOLS.md"));
        mock.Setup(w => w.GetUserFile()).Returns(Path.Combine(testDir, "USER.md"));
        mock.Setup(w => w.GetHeartbeatFile()).Returns(Path.Combine(testDir, "HEARTBEAT.md"));
        mock.Setup(w => w.FileExists(It.IsAny<string>())).Returns(false);
        mock.Setup(w => w.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        mock.Setup(w => w.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(w => w.AppendFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(w => w.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(w => w.EnsureDirectory(It.IsAny<string>()));
        return mock;
    }

    private static Mock<IWorkspaceManager> CreateWorkspaceMockWithSessionsDir(string sessionsDir)
    {
        var testDir = Path.GetDirectoryName(sessionsDir)!;
        Directory.CreateDirectory(Path.Combine(testDir, "memory"));
        Directory.CreateDirectory(Path.Combine(testDir, "skills"));

        File.WriteAllText(Path.Combine(testDir, "AGENTS.md"), "# Test Agent");
        File.WriteAllText(Path.Combine(testDir, "SOUL.md"), "# Test Soul");

        var mock = new Mock<IWorkspaceManager>();
        mock.Setup(w => w.GetWorkspacePath()).Returns(testDir);
        mock.Setup(w => w.GetSessionsPath()).Returns(sessionsDir);
        mock.Setup(w => w.GetAgentsFile()).Returns(Path.Combine(testDir, "AGENTS.md"));
        mock.Setup(w => w.GetSoulFile()).Returns(Path.Combine(testDir, "SOUL.md"));
        mock.Setup(w => w.GetMemoryFile()).Returns(Path.Combine(testDir, "memory", "MEMORY.md"));
        mock.Setup(w => w.GetHistoryFile()).Returns(Path.Combine(testDir, "memory", "HISTORY.md"));
        mock.Setup(w => w.GetSkillsPath()).Returns(Path.Combine(testDir, "skills"));
        mock.Setup(w => w.GetMemoryPath()).Returns(Path.Combine(testDir, "memory"));
        mock.Setup(w => w.GetToolsFile()).Returns(Path.Combine(testDir, "TOOLS.md"));
        mock.Setup(w => w.GetUserFile()).Returns(Path.Combine(testDir, "USER.md"));
        mock.Setup(w => w.GetHeartbeatFile()).Returns(Path.Combine(testDir, "HEARTBEAT.md"));
        mock.Setup(w => w.FileExists(It.IsAny<string>())).Returns(false);
        mock.Setup(w => w.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        mock.Setup(w => w.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(w => w.AppendFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(w => w.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(w => w.EnsureDirectory(It.IsAny<string>()));
        return mock;
    }

    private static Mock<ISkillsLoader> CreateSkillsLoaderMock()
    {
        var mock = new Mock<ISkillsLoader>();
        mock.Setup(s => s.GetLoadedSkills()).Returns([]);
        mock.Setup(s => s.ListSkills(It.IsAny<bool>())).Returns([]);
        mock.Setup(s => s.GetAlwaysSkills()).Returns([]);
        mock.Setup(s => s.BuildSkillsSummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);
        mock.Setup(s => s.LoadSkillsForContextAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);
        return mock;
    }

    private static Mock<IChannel> CreateMockChannel(string id, string type)
    {
        var mock = new Mock<IChannel>();
        mock.Setup(c => c.Id).Returns(id);
        mock.Setup(c => c.Type).Returns(type);
        mock.Setup(c => c.IsConnected).Returns(true);
        mock.Setup(c => c.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mock.Setup(c => c.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mock.Setup(c => c.SendMessageAsync(It.IsAny<OutboundMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }
}
