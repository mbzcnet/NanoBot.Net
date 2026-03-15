using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NanoBot.Agent;
using NanoBot.Core.Bus;
using NanoBot.Core.Configuration;
using NanoBot.Core.Tools;
using NanoBot.Core.Tools.Browser;
using NanoBot.Core.Workspace;
using NanoBot.Infrastructure.Browser;
using NanoBot.Tools.BuiltIn;
using Xunit;

namespace NanoBot.Agent.Tests.Integration;

public class AgentBrowserSnapshotIntegrationTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly Mock<IChatClient> _chatClientMock;
    private readonly Mock<IBrowserService> _browserServiceMock;
    private readonly Mock<IMessageBus> _busMock;
    private readonly Mock<ISessionManager> _sessionManagerMock;
    private readonly Mock<IWorkspaceManager> _workspaceMock;
    private readonly Mock<ILogger<AgentRuntime>> _loggerMock;
    private readonly string _sessionsPath;

    public AgentBrowserSnapshotIntegrationTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"nanobot_agent_snapshot_tests_{Guid.NewGuid():N}");
        _sessionsPath = Path.Combine(_testDirectory, "sessions");
        Directory.CreateDirectory(_sessionsPath);

        _chatClientMock = new Mock<IChatClient>();
        _browserServiceMock = new Mock<IBrowserService>();
        _busMock = new Mock<IMessageBus>();
        _sessionManagerMock = new Mock<ISessionManager>();
        _workspaceMock = new Mock<IWorkspaceManager>();
        _loggerMock = new Mock<ILogger<AgentRuntime>>();

        _workspaceMock.Setup(w => w.GetSessionsPath()).Returns(_sessionsPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> ToAsyncEnumerable(IEnumerable<ChatResponseUpdate> updates)
    {
        foreach (var update in updates)
        {
            yield return update;
            await Task.Yield();
        }
    }

    [Fact]
    public async Task ProcessDirectStreamingAsync_WithBrowserSnapshot_ShouldInjectImageMarkdown()
    {
        // Arrange
        var sessionKey = "test-session";
        var imageFileName = "snapshot.png";
        var sessionDir = Path.Combine(_sessionsPath, sessionKey);
        var screenshotsDir = Path.Combine(sessionDir, "screenshots");
        Directory.CreateDirectory(screenshotsDir);
        var imagePath = Path.Combine(screenshotsDir, imageFileName);
        
        // Create a dummy image file
        await File.WriteAllBytesAsync(imagePath, new byte[] { 0x00, 0x01, 0x02 });

        // Setup BrowserService mock to return a successful snapshot response
        _browserServiceMock.Setup(s => s.CaptureSnapshotAsync(
            It.IsAny<string>(), 
            It.IsAny<string>(), 
            It.IsAny<string>(), 
            It.IsAny<string?>(), 
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, string fmt, string prof, string? key, CancellationToken ct) => 
            {
                var effectiveKey = key ?? "fallback_nanobot"; // Use fallback if null
                return new BrowserToolResponse
                {
                    Ok = true,
                    Action = "snapshot",
                    ImagePath = Path.Combine(_sessionsPath, effectiveKey, "screenshots", imageFileName)
                };
            });

        // Setup ChatClient mock to simulate LLM calling the tool
        // First call: returns FunctionCall
        // Second call: returns text response
        var arguments = new Dictionary<string, object?>
        {
            ["action"] = "snapshot",
            ["targetId"] = "tab1"
        };
        // FunctionCallContent(string callId, string name, IDictionary<string, object?>? arguments = null)
        var functionCall = new FunctionCallContent("call_123", "browser", arguments);
        
        // Simulate streaming response for the function call
        var callCount = 0;
        _chatClientMock.Setup(c => c.GetStreamingResponseAsync(
            It.IsAny<IList<ChatMessage>>(),
            It.IsAny<ChatOptions>(),
            It.IsAny<CancellationToken>()))
            .Returns((IList<ChatMessage> messages, ChatOptions options, CancellationToken ct) => 
            {
                callCount++;
                if (callCount == 1)
                {
                    return ToAsyncEnumerable(new[] { 
                        new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = { functionCall } } 
                    });
                }
                else
                {
                    return ToAsyncEnumerable(new[] { 
                        new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = { new TextContent("Snapshot taken.") } } 
                    });
                }
            });

        _chatClientMock.Setup(c => c.GetService(typeof(ChatClientMetadata), null))
            .Returns(new ChatClientMetadata("test"));

        // Create Agent with Browser Tool
        var browserTool = BrowserTools.CreateBrowserTool(_browserServiceMock.Object);
        var agentOptions = new ChatClientAgentOptions
        {
            Name = "NanoBot",
            Description = "Test Agent",
            ChatOptions = new ChatOptions
            {
                Tools = [browserTool]
            }
        };
        var agent = new ChatClientAgent(_chatClientMock.Object, agentOptions);

        // Setup SessionManager to return a valid session
        var session = await agent.CreateSessionAsync();
        _sessionManagerMock.Setup(s => s.GetOrCreateSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Create Runtime
        var runtime = new AgentRuntime(
            agent, 
            _busMock.Object, 
            _sessionManagerMock.Object, 
            _workspaceMock.Object, 
            null, 
            null, 
            50, 
            null, 
            null, 
            null, 
            _loggerMock.Object);

        // Act
        var updates = new List<AgentResponseUpdate>();
        await foreach (var update in runtime.ProcessDirectStreamingAsync("Take a snapshot", sessionKey))
        {
            updates.Add(update);
        }

        // Verify BrowserService was called with ANY sessionKey (null is acceptable due to AsyncLocal loss in mock)
        _browserServiceMock.Verify(s => s.CaptureSnapshotAsync(
            It.IsAny<string>(), 
            It.IsAny<string>(), 
            It.IsAny<string>(), 
            It.IsAny<string?>(), 
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify Logger warnings
        _loggerMock.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("BuildSnapshotImageMarkdown")),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);

        // Assert
        // Check if we received the snapshot injection
        var snapshotUpdate = updates.FirstOrDefault(u => u.AdditionalProperties?.ContainsKey("_snapshot_image") == true);
        Assert.NotNull(snapshotUpdate);
        
        var markdown = snapshotUpdate.Contents.OfType<TextContent>().FirstOrDefault()?.Text;
        Assert.NotNull(markdown);
        Assert.Contains("![snapshot-1](/api/files/sessions/", markdown);
        Assert.Contains(imageFileName, markdown);
    }
}
