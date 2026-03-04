using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using NanoBot.Agent.Extensions;
using NanoBot.Core.Workspace;
using Xunit;

namespace NanoBot.Agent.Tests;

public class SessionMessageTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly ChatClientAgent _agent;
    private readonly Mock<IWorkspaceManager> _workspaceMock;
    private readonly Mock<ILogger<SessionManager>> _loggerMock;

    public SessionMessageTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"nanobot_message_tests_{Guid.NewGuid():N}");
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
    public async Task SaveSessionAsync_SavesMessagesToFile()
    {
        var manager = new SessionManager(_agent, _workspaceMock.Object, _loggerMock.Object);
        var session = await manager.GetOrCreateSessionAsync("test:messages");

        var userMessage = new ChatMessage(ChatRole.User, "Hello, this is a test message");
        session.StateBag.SetValue("FileBackedChatHistoryProvider", new List<ChatMessage> { userMessage });

        await manager.SaveSessionAsync(session, "test:messages");

        var sessionFile = Path.Combine(_testDirectory, "sessions", "test_messages.jsonl");
        Assert.True(File.Exists(sessionFile));

        var lines = await File.ReadAllLinesAsync(sessionFile);
        Assert.Contains(lines, line => line.Contains("Hello, this is a test message"));
    }

    [Fact]
    public async Task SaveSessionAsync_SavesMultipleMessages()
    {
        var manager = new SessionManager(_agent, _workspaceMock.Object, _loggerMock.Object);
        var session = await manager.GetOrCreateSessionAsync("test:multiple");

        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.User, "First message"),
            new ChatMessage(ChatRole.Assistant, "First response"),
            new ChatMessage(ChatRole.User, "Second message"),
            new ChatMessage(ChatRole.Assistant, "Second response")
        };
        session.StateBag.SetValue("FileBackedChatHistoryProvider", messages);

        await manager.SaveSessionAsync(session, "test:multiple");

        var sessionFile = Path.Combine(_testDirectory, "sessions", "test_multiple.jsonl");
        var lines = await File.ReadAllLinesAsync(sessionFile);

        Assert.Contains(lines, line => line.Contains("First message"));
        Assert.Contains(lines, line => line.Contains("First response"));
        Assert.Contains(lines, line => line.Contains("Second message"));
        Assert.Contains(lines, line => line.Contains("Second response"));
    }

    [Fact]
    public async Task LoadSessionAsync_RestoresMessagesFromFile()
    {
        var manager = new SessionManager(_agent, _workspaceMock.Object, _loggerMock.Object);
        var session = await manager.GetOrCreateSessionAsync("test:load");

        var originalMessages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.User, "Original message"),
            new ChatMessage(ChatRole.Assistant, "Original response")
        };
        session.StateBag.SetValue("FileBackedChatHistoryProvider", originalMessages);

        await manager.SaveSessionAsync(session, "test:load");

        await manager.InvalidateAsync("test:load");
        var loadedSession = await manager.GetOrCreateSessionAsync("test:load");

        var loadedMessages = loadedSession.GetAllMessages();
        Assert.Equal(2, loadedMessages.Count);
        Assert.Contains(loadedMessages, m => m.Text.Contains("Original message"));
        Assert.Contains(loadedMessages, m => m.Text.Contains("Original response"));
    }

    [Fact]
    public async Task GetAllMessages_RetrievesMessagesFromStateBag()
    {
        var manager = new SessionManager(_agent, _workspaceMock.Object, _loggerMock.Object);
        var session = await manager.GetOrCreateSessionAsync("test:retrieve");

        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.User, "Test message 1"),
            new ChatMessage(ChatRole.Assistant, "Test response 1"),
            new ChatMessage(ChatRole.User, "Test message 2")
        };
        session.StateBag.SetValue("FileBackedChatHistoryProvider", messages);

        var retrievedMessages = session.GetAllMessages();

        Assert.Equal(3, retrievedMessages.Count);
        Assert.Equal("Test message 1", retrievedMessages[0].Text);
        Assert.Equal("Test response 1", retrievedMessages[1].Text);
        Assert.Equal("Test message 2", retrievedMessages[2].Text);
    }

    [Fact]
    public async Task GetAllMessages_ReturnsEmpty_WhenNoMessages()
    {
        var manager = new SessionManager(_agent, _workspaceMock.Object, _loggerMock.Object);
        var session = await manager.GetOrCreateSessionAsync("test:empty");

        var messages = session.GetAllMessages();

        Assert.Empty(messages);
    }

    [Fact]
    public async Task GetAllMessages_ReturnsEmpty_WhenSessionIsNull()
    {
        AgentSession? nullSession = null;

        var messages = nullSession?.GetAllMessages() ?? Array.Empty<ChatMessage>();

        Assert.Empty(messages);
    }

    [Fact]
    public async Task SaveSessionAsync_PreservesMessageFormat()
    {
        var manager = new SessionManager(_agent, _workspaceMock.Object, _loggerMock.Object);
        var session = await manager.GetOrCreateSessionAsync("test:format");

        var message = new ChatMessage(ChatRole.User, "Test message");
        message = message.WithAgentRequestMessageSource(AgentRequestMessageSourceType.External, "user");

        var messages = new List<ChatMessage> { message };
        session.StateBag.SetValue("FileBackedChatHistoryProvider", messages);

        await manager.SaveSessionAsync(session, "test:format");

        var sessionFile = Path.Combine(_testDirectory, "sessions", "test_format.jsonl");
        var lines = await File.ReadAllLinesAsync(sessionFile);

        var jsonLines = lines.Where(l => !string.IsNullOrWhiteSpace(l) && !l.Contains("_type")).ToList();
        Assert.Single(jsonLines);

        var msg = JsonSerializer.Deserialize<JsonElement>(jsonLines[0]);
        Assert.True(msg.TryGetProperty("role", out var roleElement));
        Assert.Equal("user", roleElement.GetString());
    }

    [Fact]
    public async Task SaveSessionAsync_IncludesMetadata()
    {
        var manager = new SessionManager(_agent, _workspaceMock.Object, _loggerMock.Object);
        var session = await manager.GetOrCreateSessionAsync("test:metadata");

        manager.SetSessionTitle("test:metadata", "Test Session Title");
        manager.SetSessionProfileId("test:metadata", "test-profile");

        await manager.SaveSessionAsync(session, "test:metadata");

        var sessionFile = Path.Combine(_testDirectory, "sessions", "test_metadata.jsonl");
        var lines = await File.ReadAllLinesAsync(sessionFile);

        var metadataLine = lines.FirstOrDefault(l => l.Contains("_type"));
        Assert.NotNull(metadataLine);

        var metadata = JsonSerializer.Deserialize<JsonElement>(metadataLine);
        Assert.True(metadata.TryGetProperty("_type", out var typeElement));
        Assert.Equal("metadata", typeElement.GetString());
    }

    [Fact]
    public async Task SaveAndLoadSession_RoundTrip()
    {
        var manager = new SessionManager(_agent, _workspaceMock.Object, _loggerMock.Object);

        var originalSession = await manager.GetOrCreateSessionAsync("test:roundtrip");
        manager.SetSessionTitle("test:roundtrip", "Roundtrip Test");
        manager.SetSessionProfileId("test:roundtrip", "profile-123");

        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.User, "User message"),
            new ChatMessage(ChatRole.Assistant, "Assistant response"),
            new ChatMessage(ChatRole.User, "Follow-up question"),
            new ChatMessage(ChatRole.Assistant, "Follow-up answer")
        };
        originalSession.StateBag.SetValue("FileBackedChatHistoryProvider", messages);

        await manager.SaveSessionAsync(originalSession, "test:roundtrip");

        await manager.InvalidateAsync("test:roundtrip");
        var loadedSession = await manager.GetOrCreateSessionAsync("test:roundtrip");

        var loadedMessages = loadedSession.GetAllMessages();
        Assert.Equal(4, loadedMessages.Count);
        Assert.Equal("User message", loadedMessages[0].Text);
        Assert.Equal("Assistant response", loadedMessages[1].Text);
        Assert.Equal("Follow-up question", loadedMessages[2].Text);
        Assert.Equal("Follow-up answer", loadedMessages[3].Text);
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
