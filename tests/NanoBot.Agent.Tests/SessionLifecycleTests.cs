using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using NanoBot.Agent.Extensions;
using NanoBot.Core.Workspace;
using Xunit;

namespace NanoBot.Agent.Tests;

public class SessionLifecycleTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly ChatClientAgent _agent;
    private readonly Mock<IWorkspaceManager> _workspaceMock;
    private readonly Mock<ILogger<SessionManager>> _loggerMock;

    public SessionLifecycleTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"nanobot_lifecycle_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
        Directory.CreateDirectory(Path.Combine(_testDirectory, "sessions"));

        _agent = CreateAgent();
        _workspaceMock = CreateWorkspaceMock(_testDirectory);
        _loggerMock = new Mock<ILogger<SessionManager>>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SessionLifecycle_CreateSaveLoadDelete()
    {
        var manager = new SessionManager(_agent, _workspaceMock.Object, _loggerMock.Object);

        var sessionKey = "lifecycle:test";
        var session = await manager.GetOrCreateSessionAsync(sessionKey);

        manager.SetSessionTitle(sessionKey, "Lifecycle Test Session");
        manager.SetSessionProfileId(sessionKey, "test-profile");

        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.User, "First message"),
            new ChatMessage(ChatRole.Assistant, "First response"),
            new ChatMessage(ChatRole.User, "Second message"),
            new ChatMessage(ChatRole.Assistant, "Second response")
        };
        session.StateBag.SetValue("FileBackedChatHistoryProvider", messages);

        await manager.SaveSessionAsync(session, sessionKey);

        var sessionFile = Path.Combine(_testDirectory, "sessions", "lifecycle_test.jsonl");
        Assert.True(File.Exists(sessionFile));

        await manager.InvalidateAsync(sessionKey);
        var loadedSession = await manager.GetOrCreateSessionAsync(sessionKey);

        var loadedMessages = loadedSession.GetAllMessages();
        Assert.Equal(4, loadedMessages.Count);

        await manager.ClearSessionAsync(sessionKey);
        Assert.False(File.Exists(sessionFile));
    }

    [Fact]
    public async Task MultipleSessions_IndependentStorage()
    {
        var manager = new SessionManager(_agent, _workspaceMock.Object, _loggerMock.Object);

        var session1 = await manager.GetOrCreateSessionAsync("multi:session1");
        var session2 = await manager.GetOrCreateSessionAsync("multi:session2");

        session1.StateBag.SetValue("FileBackedChatHistoryProvider",
            new List<ChatMessage> { new ChatMessage(ChatRole.User, "Session 1 message") });
        session2.StateBag.SetValue("FileBackedChatHistoryProvider",
            new List<ChatMessage> { new ChatMessage(ChatRole.User, "Session 2 message") });

        await manager.SaveSessionAsync(session1, "multi:session1");
        await manager.SaveSessionAsync(session2, "multi:session2");

        await manager.InvalidateAsync("multi:session1");
        await manager.InvalidateAsync("multi:session2");

        var loaded1 = await manager.GetOrCreateSessionAsync("multi:session1");
        var loaded2 = await manager.GetOrCreateSessionAsync("multi:session2");

        var messages1 = loaded1.GetAllMessages();
        var messages2 = loaded2.GetAllMessages();

        Assert.Single(messages1);
        Assert.Single(messages2);
        Assert.Contains("Session 1 message", messages1[0].Text);
        Assert.Contains("Session 2 message", messages2[0].Text);
    }

    [Fact]
    public async Task SessionTitle_PersistsAcrossReload()
    {
        var manager = new SessionManager(_agent, _workspaceMock.Object, _loggerMock.Object);

        var sessionKey = "title:test";
        var session = await manager.GetOrCreateSessionAsync(sessionKey);

        manager.SetSessionTitle(sessionKey, "Original Title");
        await manager.SaveSessionAsync(session, sessionKey);

        await manager.InvalidateAsync(sessionKey);
        var loadedSession = await manager.GetOrCreateSessionAsync(sessionKey);

        var sessions = manager.ListSessions();
        var loadedSessionInfo = sessions.FirstOrDefault(s => s.Key == sessionKey);

        Assert.NotNull(loadedSessionInfo);
        Assert.Equal("Original Title", loadedSessionInfo.Title);
    }

    [Fact]
    public async Task SessionProfileId_PersistsAcrossReload()
    {
        var manager = new SessionManager(_agent, _workspaceMock.Object, _loggerMock.Object);

        var sessionKey = "profile:test";
        var session = await manager.GetOrCreateSessionAsync(sessionKey);

        manager.SetSessionProfileId(sessionKey, "test-profile-123");
        await manager.SaveSessionAsync(session, sessionKey);

        await manager.InvalidateAsync(sessionKey);
        var loadedSession = await manager.GetOrCreateSessionAsync(sessionKey);

        var sessions = manager.ListSessions();
        var loadedSessionInfo = sessions.FirstOrDefault(s => s.Key == sessionKey);

        Assert.NotNull(loadedSessionInfo);
        Assert.Equal("test-profile-123", loadedSessionInfo.ProfileId);
    }

    [Fact]
    public async Task ListSessions_OrdersByUpdatedAt()
    {
        var manager = new SessionManager(_agent, _workspaceMock.Object, _loggerMock.Object);

        var session1 = await manager.GetOrCreateSessionAsync("order:session1");
        var session2 = await manager.GetOrCreateSessionAsync("order:session2");
        var session3 = await manager.GetOrCreateSessionAsync("order:session3");

        session1.StateBag.SetValue("FileBackedChatHistoryProvider",
            new List<ChatMessage> { new ChatMessage(ChatRole.User, "Message 1") });
        session2.StateBag.SetValue("FileBackedChatHistoryProvider",
            new List<ChatMessage> { new ChatMessage(ChatRole.User, "Message 2") });
        session3.StateBag.SetValue("FileBackedChatHistoryProvider",
            new List<ChatMessage> { new ChatMessage(ChatRole.User, "Message 3") });

        await manager.SaveSessionAsync(session1, "order:session1");
        await Task.Delay(100);
        await manager.SaveSessionAsync(session2, "order:session2");
        await Task.Delay(100);
        await manager.SaveSessionAsync(session3, "order:session3");

        var sessions = manager.ListSessions().ToList();

        Assert.Equal(3, sessions.Count);
        Assert.Equal("order:session3", sessions[0].Key);
        Assert.Equal("order:session2", sessions[1].Key);
        Assert.Equal("order:session1", sessions[2].Key);
    }

    [Fact]
    public async Task ClearSession_RemovesFileAndCache()
    {
        var manager = new SessionManager(_agent, _workspaceMock.Object, _loggerMock.Object);

        var sessionKey = "clear:test";
        var session = await manager.GetOrCreateSessionAsync(sessionKey);

        session.StateBag.SetValue("FileBackedChatHistoryProvider",
            new List<ChatMessage> { new ChatMessage(ChatRole.User, "Test message") });

        await manager.SaveSessionAsync(session, sessionKey);

        var sessionFile = Path.Combine(_testDirectory, "sessions", "clear_test.jsonl");
        Assert.True(File.Exists(sessionFile));

        await manager.ClearSessionAsync(sessionKey);

        Assert.False(File.Exists(sessionFile));

        var newSession = await manager.GetOrCreateSessionAsync(sessionKey);
        var messages = newSession.GetAllMessages();

        Assert.Empty(messages);
    }

    [Fact]
    public async Task InvalidateForcesReloadFromFile()
    {
        var manager = new SessionManager(_agent, _workspaceMock.Object, _loggerMock.Object);

        var sessionKey = "invalidate:test";
        var session = await manager.GetOrCreateSessionAsync(sessionKey);

        session.StateBag.SetValue("FileBackedChatHistoryProvider",
            new List<ChatMessage> { new ChatMessage(ChatRole.User, "Original message") });

        await manager.SaveSessionAsync(session, sessionKey);

        await manager.InvalidateAsync(sessionKey);
        var reloadedSession = await manager.GetOrCreateSessionAsync(sessionKey);

        var messages = reloadedSession.GetAllMessages();
        Assert.Contains(messages, m => m.Text.Contains("Original message"));
    }

    [Fact]
    public async Task WebUISessionKey_SupportsColon()
    {
        var manager = new SessionManager(_agent, _workspaceMock.Object, _loggerMock.Object);

        var sessionId = Guid.NewGuid().ToString("N");
        var sessionKey = $"webui:{sessionId}";
        var session = await manager.GetOrCreateSessionAsync(sessionKey);

        manager.SetSessionTitle(sessionKey, "WebUI Test Session");

        session.StateBag.SetValue("FileBackedChatHistoryProvider",
            new List<ChatMessage> { new ChatMessage(ChatRole.User, "WebUI message") });

        await manager.SaveSessionAsync(session, sessionKey);

        var expectedFile = Path.Combine(_testDirectory, "sessions", $"webui_{sessionId}.jsonl");
        Assert.True(File.Exists(expectedFile));

        await manager.InvalidateAsync(sessionKey);
        var loadedSession = await manager.GetOrCreateSessionAsync(sessionKey);

        var messages = loadedSession.GetAllMessages();
        Assert.Single(messages);
        Assert.Contains("WebUI message", messages[0].Text);
    }

    [Fact]
    public async Task EmptySession_CreatesValidFile()
    {
        var manager = new SessionManager(_agent, _workspaceMock.Object, _loggerMock.Object);

        var sessionKey = "empty:test";
        var session = await manager.GetOrCreateSessionAsync(sessionKey);

        manager.SetSessionTitle(sessionKey, "Empty Session");

        await manager.SaveSessionAsync(session, sessionKey);

        var sessionFile = Path.Combine(_testDirectory, "sessions", "empty_test.jsonl");
        Assert.True(File.Exists(sessionFile));

        var lines = await File.ReadAllLinesAsync(sessionFile);
        var metadataLine = lines.FirstOrDefault(l => l.Contains("_type"));
        Assert.NotNull(metadataLine);
    }

    private static ChatClientAgent CreateAgent()
    {
        var chatClientMock = new Mock<IChatClient>();
        var metadata = new ChatClientMetadata("test");
        chatClientMock.Setup(c => c.GetService(typeof(ChatClientMetadata), null))
            .Returns(metadata);

        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Test response"));
        chatClientMock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var options = new ChatClientAgentOptions
        {
            Name = "TestAgent",
            Description = "Test Description"
        };

        return new ChatClientAgent(chatClientMock.Object, options);
    }

    private static Mock<IWorkspaceManager> CreateWorkspaceMock(string testDir)
    {
        var mock = new Mock<IWorkspaceManager>();
        mock.Setup(w => w.GetSessionsPath()).Returns(Path.Combine(testDir, "sessions"));
        return mock;
    }
}
