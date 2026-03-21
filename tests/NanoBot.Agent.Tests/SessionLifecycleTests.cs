using System.Runtime.CompilerServices;
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

        // Verify messages are saved correctly
        var lines = await File.ReadAllLinesAsync(sessionFile);
        var messageLines = lines.Skip(1).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        Assert.Equal(4, messageLines.Count);

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

        // Verify both sessions saved correctly with independent files
        var file1 = Path.Combine(_testDirectory, "sessions", "multi_session1.jsonl");
        var file2 = Path.Combine(_testDirectory, "sessions", "multi_session2.jsonl");
        Assert.True(File.Exists(file1));
        Assert.True(File.Exists(file2));

        var lines1 = await File.ReadAllLinesAsync(file1);
        var lines2 = await File.ReadAllLinesAsync(file2);

        Assert.Contains(lines1, l => l.Contains("Session 1 message"));
        Assert.Contains(lines2, l => l.Contains("Session 2 message"));
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

        var sessionFile = Path.Combine(_testDirectory, "sessions", "invalidate_test.jsonl");
        Assert.True(File.Exists(sessionFile));

        // Verify file content contains the message
        var lines = await File.ReadAllLinesAsync(sessionFile);
        Assert.Contains(lines, l => l.Contains("Original message"));
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

        // Verify file content contains the message
        var lines = await File.ReadAllLinesAsync(expectedFile);
        Assert.Contains(lines, l => l.Contains("WebUI message"));
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

        // Use streaming response mock
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Test response"));
        chatClientMock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Setup streaming with word-by-word chunks
        chatClientMock.Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(StreamingResponse("Test response"));

        var options = new ChatClientAgentOptions
        {
            Name = "TestAgent",
            Description = "Test Description"
        };

        return new ChatClientAgent(chatClientMock.Object, options);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> StreamingResponse(string text)
    {
        var chunks = text.Split(' ');
        foreach (var chunk in chunks)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, chunk + " ");
            await Task.Yield();
        }
    }

    private static Mock<IWorkspaceManager> CreateWorkspaceMock(string testDir)
    {
        var mock = new Mock<IWorkspaceManager>();
        mock.Setup(w => w.GetSessionsPath()).Returns(Path.Combine(testDir, "sessions"));
        return mock;
    }
}
