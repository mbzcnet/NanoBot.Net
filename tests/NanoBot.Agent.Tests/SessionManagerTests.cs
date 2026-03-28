using System.Runtime.CompilerServices;
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
    public async Task SaveSessionAsync_WritesAgentSessionIntoMetadata()
    {
        var manager = new SessionManager(_agent, _workspaceMock.Object, _loggerMock.Object);
        var sessionKey = "test:metadata_agent_session";
        var session = await manager.GetOrCreateSessionAsync(sessionKey);

        await manager.SaveSessionAsync(session, sessionKey);

        var sessionFile = Path.Combine(_testDirectory, "sessions", "test_metadata_agent_session.jsonl");
        var firstLine = (await File.ReadAllLinesAsync(sessionFile)).First();
        var metadata = JsonSerializer.Deserialize<JsonElement>(firstLine);

        Assert.True(metadata.TryGetProperty("metadata", out var innerMetadata));
        Assert.True(innerMetadata.TryGetProperty("agent_session", out _));
    }

    [Fact]
    public async Task GetOrCreateSessionAsync_RestoresStructuredToolMessages_FromLegacyJsonlFormat()
    {
        var manager = new SessionManager(_agent, _workspaceMock.Object, _loggerMock.Object);
        var sessionKey = "test:legacy_tool_restore";
        var sessionFile = Path.Combine(_testDirectory, "sessions", "test_legacy_tool_restore.jsonl");

        var metadata = new
        {
            _type = "metadata",
            key = sessionKey,
            created_at = DateTimeOffset.UtcNow.ToString("o"),
            updated_at = DateTimeOffset.UtcNow.ToString("o"),
            title = "legacy",
            profile_id = (string?)null,
            last_consolidated = 0
        };

        var assistantMessage = new
        {
            role = "assistant",
            content = string.Empty,
            timestamp = DateTimeOffset.UtcNow.ToString("o"),
            tool_calls = new[]
            {
                new
                {
                    id = "call_legacy_1",
                    type = "function",
                    function = new
                    {
                        name = "browser",
                        arguments = "{\"action\":\"open\",\"targetUrl\":\"https://example.com\"}"
                    }
                }
            }
        };

        var toolMessage = new
        {
            role = "tool",
            content = "{\"ok\":true}",
            timestamp = DateTimeOffset.UtcNow.ToString("o"),
            tool_call_id = "call_legacy_1"
        };

        await File.WriteAllLinesAsync(sessionFile,
        [
            JsonSerializer.Serialize(metadata),
            JsonSerializer.Serialize(assistantMessage),
            JsonSerializer.Serialize(toolMessage)
        ]);

        var loadedSession = await manager.GetOrCreateSessionAsync(sessionKey);

        Assert.True(loadedSession.StateBag.TryGetValue<List<ChatMessage>>("ChatHistoryProvider", out var messages));
        Assert.NotNull(messages);
        Assert.Equal(2, messages!.Count);
        Assert.Contains(messages[0].Contents, c => c is FunctionCallContent call && call.CallId == "call_legacy_1" && call.Name == "browser");
        Assert.Contains(messages[1].Contents, c => c is FunctionResultContent result && result.CallId == "call_legacy_1");
    }

    [Fact]
    public async Task SaveSessionAsync_RemovesToolCallMarkersFromContent()
    {
        var manager = new SessionManager(_agent, _workspaceMock.Object, _loggerMock.Object);
        var sessionKey = "test:tool_call_markers";
        var session = await manager.GetOrCreateSessionAsync(sessionKey);

        if (!session.StateBag.TryGetValue<List<ChatMessage>>("ChatHistoryProvider", out var list) || list == null)
        {
            list = new List<ChatMessage>();
            session.StateBag.SetValue("ChatHistoryProvider", list);
        }

        // Add a message with tool call and content containing [TOOL_CALL] markers
        var args = new Dictionary<string, object?> { { "query", "test" } };
        var toolCall = new FunctionCallContent("call_1", "search", args);

        // Create content with [TOOL_CALL] markers (as they would appear in the display)
        var contentWithMarkers = "\n[TOOL_CALL]search(\"test\")[/TOOL_CALL]\n\nHere are the results:";
        var callMessage = new ChatMessage(ChatRole.Assistant, contentWithMarkers)
        {
            Contents = { toolCall }
        };
        list.Add(callMessage);

        await manager.SaveSessionAsync(session, sessionKey);

        // Read file content
        var sessionFile = Path.Combine(_testDirectory, "sessions", "test_tool_call_markers.jsonl");
        Assert.True(File.Exists(sessionFile));

        var lines = await File.ReadAllLinesAsync(sessionFile);
        var json = JsonSerializer.Deserialize<JsonElement>(lines.Last());

        // Verify content has markers removed
        var content = json.GetProperty("content").GetString();
        Assert.DoesNotContain("[TOOL_CALL]", content);
        Assert.DoesNotContain("[/TOOL_CALL]", content);

        // Verify tool_calls are still preserved
        Assert.True(json.TryGetProperty("tool_calls", out var savedToolCalls));
        Assert.Equal(1, savedToolCalls.GetArrayLength());
    }

    [Fact]
    public async Task SaveSessionAsync_CleansMultipleBlankLines()
    {
        var manager = new SessionManager(_agent, _workspaceMock.Object, _loggerMock.Object);
        var sessionKey = "test:blank_lines";
        var session = await manager.GetOrCreateSessionAsync(sessionKey);

        if (!session.StateBag.TryGetValue<List<ChatMessage>>("ChatHistoryProvider", out var list) || list == null)
        {
            list = new List<ChatMessage>();
            session.StateBag.SetValue("ChatHistoryProvider", list);
        }

        // Add a message with tool call and multiple blank lines
        var args = new Dictionary<string, object?> { { "query", "test" } };
        var toolCall = new FunctionCallContent("call_1", "search", args);

        // Create content with multiple blank lines after markers
        var contentWithMultipleBlanks = "\n[TOOL_CALL]search(\"test\")[/TOOL_CALL]\n\n\n\nSome content";
        var callMessage = new ChatMessage(ChatRole.Assistant, contentWithMultipleBlanks)
        {
            Contents = { toolCall }
        };
        list.Add(callMessage);

        await manager.SaveSessionAsync(session, sessionKey);

        var sessionFile = Path.Combine(_testDirectory, "sessions", "test_blank_lines.jsonl");
        var lines = await File.ReadAllLinesAsync(sessionFile);
        var json = JsonSerializer.Deserialize<JsonElement>(lines.Last());

        var content = json.GetProperty("content").GetString();
        // Should have at most 2 consecutive newlines, not 4+
        Assert.DoesNotContain("\n\n\n", content);
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
