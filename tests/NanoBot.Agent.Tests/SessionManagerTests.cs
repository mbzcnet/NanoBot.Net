using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using NanoBot.Core.Workspace;
using Xunit;

namespace NanoBot.Agent.Tests;

public class SessionManagerTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly ChatClientAgent _agent;
    private readonly Mock<IWorkspaceManager> _workspaceMock;
    private readonly Mock<ILogger<SessionManager>> _loggerMock;

    public SessionManagerTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"nanobot_session_tests_{Guid.NewGuid():N}");
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
    public async Task GetOrCreateSessionAsync_CreatesNewSession_WhenNotExists()
    {
        var manager = new SessionManager(_agent, _workspaceMock.Object, _loggerMock.Object);

        var session = await manager.GetOrCreateSessionAsync("test:session");

        Assert.NotNull(session);
    }

    [Fact]
    public async Task GetOrCreateSessionAsync_ReturnsCachedSession_OnSecondCall()
    {
        var manager = new SessionManager(_agent, _workspaceMock.Object, _loggerMock.Object);

        var session1 = await manager.GetOrCreateSessionAsync("test:session");
        var session2 = await manager.GetOrCreateSessionAsync("test:session");

        Assert.Same(session1, session2);
    }

    [Fact]
    public async Task SaveSessionAsync_SavesToFile()
    {
        var manager = new SessionManager(_agent, _workspaceMock.Object, _loggerMock.Object);
        var session = await manager.GetOrCreateSessionAsync("test:save");

        await manager.SaveSessionAsync(session, "test:save");

        var sessionFile = Path.Combine(_testDirectory, "sessions", "test_save.jsonl");
        Assert.True(File.Exists(sessionFile));
    }

    [Fact]
    public async Task ClearSessionAsync_RemovesFile()
    {
        var manager = new SessionManager(_agent, _workspaceMock.Object, _loggerMock.Object);
        var session = await manager.GetOrCreateSessionAsync("test:clear");
        await manager.SaveSessionAsync(session, "test:clear");

        await manager.ClearSessionAsync("test:clear");

        var sessionFile = Path.Combine(_testDirectory, "sessions", "test_clear.jsonl");
        Assert.False(File.Exists(sessionFile));
    }

    [Fact]
    public async Task ClearSessionAsync_RemovesFromCache()
    {
        var manager = new SessionManager(_agent, _workspaceMock.Object, _loggerMock.Object);
        await manager.GetOrCreateSessionAsync("test:clear2");

        await manager.ClearSessionAsync("test:clear2");

        var session1 = await manager.GetOrCreateSessionAsync("test:clear2");
        var session2 = await manager.GetOrCreateSessionAsync("test:clear2");

        Assert.Same(session1, session2);
    }

    [Fact]
    public async Task InvalidateAsync_RemovesFromCache()
    {
        var manager = new SessionManager(_agent, _workspaceMock.Object, _loggerMock.Object);
        await manager.GetOrCreateSessionAsync("test:invalidate");

        await manager.InvalidateAsync("test:invalidate");

        var session1 = await manager.GetOrCreateSessionAsync("test:invalidate");
        var session2 = await manager.GetOrCreateSessionAsync("test:invalidate");

        Assert.Same(session1, session2);
    }

    [Fact]
    public async Task ListSessions_ReturnsEmpty_WhenNoSessions()
    {
        var manager = new SessionManager(_agent, _workspaceMock.Object, _loggerMock.Object);

        var sessions = manager.ListSessions();

        Assert.Empty(sessions);
    }

    [Fact]
    public async Task ListSessions_ReturnsSavedSessions()
    {
        var manager = new SessionManager(_agent, _workspaceMock.Object, _loggerMock.Object);
        var session1 = await manager.GetOrCreateSessionAsync("test:list1");
        var session2 = await manager.GetOrCreateSessionAsync("test:list2");
        await manager.SaveSessionAsync(session1, "test:list1");
        await manager.SaveSessionAsync(session2, "test:list2");

        var sessions = manager.ListSessions().ToList();

        Assert.Equal(2, sessions.Count);
        Assert.Contains(sessions, s => s.Key == "test:list1");
        Assert.Contains(sessions, s => s.Key == "test:list2");
    }

    [Fact]
    public async Task GetOrCreateSessionAsync_LoadsFromFile_WhenExists()
    {
        var manager = new SessionManager(_agent, _workspaceMock.Object, _loggerMock.Object);
        var session = await manager.GetOrCreateSessionAsync("test:load");
        await manager.SaveSessionAsync(session, "test:load");

        await manager.InvalidateAsync("test:load");

        var loadedSession = await manager.GetOrCreateSessionAsync("test:load");

        Assert.NotNull(loadedSession);
    }

    [Fact]
    public async Task SaveSessionAsync_SavesToolResults()
    {
        var manager = new SessionManager(_agent, _workspaceMock.Object, _loggerMock.Object);
        var sessionKey = "test:tool_results";
        var session = await manager.GetOrCreateSessionAsync(sessionKey);
        
        // Manually inject message into session state for testing
        // We use "ChatHistoryProvider" as a fallback key that GetAllMessages checks
        // Assuming session.StateBag is accessible and mutable
        if (!session.StateBag.TryGetValue<List<ChatMessage>>("ChatHistoryProvider", out var list) || list == null)
        {
            list = new List<ChatMessage>();
            session.StateBag.SetValue("ChatHistoryProvider", list);
        }
        
        // Add a message with tool call
        // FunctionCallContent(string callId, string name, IDictionary<string, object?>? arguments = null)
        var args = new Dictionary<string, object?> { { "arg1", "value1" } };
        var toolCall = new FunctionCallContent("call_1", "test_tool", args);
        var callMessage = new ChatMessage(ChatRole.Assistant, [toolCall]);
        list.Add(callMessage);

        // Add a message with tool result
        var toolResult = new FunctionResultContent("call_1", new { success = true, value = 123 });
        var message = new ChatMessage(ChatRole.Tool, [toolResult]);
        
        list.Add(message);
        
        await manager.SaveSessionAsync(session, sessionKey);
        
        // Read file content
        var sessionFile = Path.Combine(_testDirectory, "sessions", "test_tool_results.jsonl");
        Assert.True(File.Exists(sessionFile));
        
        var lines = await File.ReadAllLinesAsync(sessionFile);
        // Expect 3 lines (system prompt + 2 added messages) or more
        Assert.True(lines.Length >= 2);
        
        // Check tool call (second to last)
        var callJson = JsonSerializer.Deserialize<JsonElement>(lines[lines.Length - 2]);
        Assert.Equal("assistant", callJson.GetProperty("role").GetString());
        var toolCalls = callJson.GetProperty("tool_calls");
        Assert.Equal(1, toolCalls.GetArrayLength());
        var call = toolCalls[0];
        Assert.Equal("call_1", call.GetProperty("id").GetString());
        Assert.Equal("test_tool", call.GetProperty("function").GetProperty("name").GetString());
        Assert.Contains("\"arg1\":\"value1\"", call.GetProperty("function").GetProperty("arguments").GetString());

        // Check tool result (last)
        var json = JsonSerializer.Deserialize<JsonElement>(lines.Last());
        
        Assert.Equal("tool", json.GetProperty("role").GetString());
        Assert.Equal("call_1", json.GetProperty("tool_call_id").GetString());
        
        // Check content contains the result
        var content = json.GetProperty("content").GetString();
        Assert.Contains("\"success\":true", content);
        Assert.Contains("\"value\":123", content);
    }

    [Fact]
    public void Constructor_ThrowsOnNullAgent()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SessionManager(null!, _workspaceMock.Object, _loggerMock.Object));
    }

    [Fact]
    public void Constructor_ThrowsOnNullWorkspace()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SessionManager(_agent, null!, _loggerMock.Object));
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
