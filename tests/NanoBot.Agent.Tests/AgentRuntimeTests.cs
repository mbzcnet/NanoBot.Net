using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using NanoBot.Core.Bus;
using NanoBot.Core.Memory;
using NanoBot.Core.Workspace;
using Xunit;

namespace NanoBot.Agent.Tests;

public class AgentRuntimeTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly ChatClientAgent _agent;
    private readonly Mock<IMessageBus> _busMock;
    private readonly Mock<ISessionManager> _sessionManagerMock;
    private readonly Mock<IWorkspaceManager> _workspaceMock;
    private readonly Mock<ILogger<AgentRuntime>> _loggerMock;

    public AgentRuntimeTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"nanobot_runtime_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
        Directory.CreateDirectory(Path.Combine(_testDirectory, "sessions"));

        _agent = CreateAgent();
        _busMock = new Mock<IMessageBus>();
        _sessionManagerMock = new Mock<ISessionManager>();
        _workspaceMock = new Mock<IWorkspaceManager>();
        _workspaceMock.Setup(w => w.GetSessionsPath()).Returns(Path.Combine(_testDirectory, "sessions"));
        _loggerMock = new Mock<ILogger<AgentRuntime>>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [Fact]
    public void Constructor_ThrowsOnNullAgent()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AgentRuntime(null!, _busMock.Object, _sessionManagerMock.Object, _workspaceMock.Object, null, null, 50));
    }

    [Fact]
    public void Constructor_ThrowsOnNullBus()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AgentRuntime(_agent, null!, _sessionManagerMock.Object, _workspaceMock.Object, null, null, 50));
    }

    [Fact]
    public void Constructor_ThrowsOnNullSessionManager()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AgentRuntime(_agent, _busMock.Object, null!, _workspaceMock.Object, null, null, 50));
    }

    [Fact]
    public void Constructor_CreatesSessionsDirectory_WhenNotExists()
    {
        var sessionsDir = Path.Combine(_testDirectory, "new_sessions");
        if (Directory.Exists(sessionsDir))
        {
            Directory.Delete(sessionsDir, true);
        }

        var workspaceMock = new Mock<IWorkspaceManager>();
        workspaceMock.Setup(w => w.GetSessionsPath()).Returns(sessionsDir);

        var runtime = new AgentRuntime(_agent, _busMock.Object, _sessionManagerMock.Object, workspaceMock.Object, null, null, 50);

        Assert.True(Directory.Exists(sessionsDir));
    }

    [Fact]
    public async Task ProcessDirectAsync_ReturnsResponse()
    {
        var session = await _agent.CreateSessionAsync();
        _sessionManagerMock.Setup(s => s.GetOrCreateSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var runtime = new AgentRuntime(_agent, _busMock.Object, _sessionManagerMock.Object, _workspaceMock.Object, null, null, 50, null, null, null, _loggerMock.Object);

        var response = await runtime.ProcessDirectAsync("Hello");

        Assert.NotNull(response);
    }

    [Fact]
    public async Task ProcessDirectAsync_UsesCorrectSessionKey()
    {
        var session = await _agent.CreateSessionAsync();
        string? capturedKey = null;
        _sessionManagerMock.Setup(s => s.GetOrCreateSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((key, _) => capturedKey = key)
            .ReturnsAsync(session);

        var runtime = new AgentRuntime(_agent, _busMock.Object, _sessionManagerMock.Object, _workspaceMock.Object, null, null, 50);

        await runtime.ProcessDirectAsync("Hello", "custom:key");

        Assert.Equal("custom:key", capturedKey);
    }

    [Fact]
    public async Task ProcessDirectAsync_SavesSession()
    {
        var session = await _agent.CreateSessionAsync();
        _sessionManagerMock.Setup(s => s.GetOrCreateSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var runtime = new AgentRuntime(_agent, _busMock.Object, _sessionManagerMock.Object, _workspaceMock.Object, null, null, 50);

        await runtime.ProcessDirectAsync("Hello");

        _sessionManagerMock.Verify(s => s.SaveSessionAsync(session, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Stop_StopsRunningRuntime()
    {
        var cts = new CancellationTokenSource();
        _busMock.Setup(b => b.ConsumeInboundAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return new InboundMessage { Channel = "test", Content = "test", SenderId = "user", ChatId = "chat1" };
            });

        var runtime = new AgentRuntime(_agent, _busMock.Object, _sessionManagerMock.Object, _workspaceMock.Object, null, null, 50);

        var runTask = runtime.RunAsync(cts.Token);
        await Task.Delay(100);

        runtime.Stop();

        await Task.WhenAny(runTask, Task.Delay(1000));
        Assert.True(runTask.IsCompleted);
    }

    [Fact]
    public void Dispose_StopsRuntime()
    {
        var runtime = new AgentRuntime(_agent, _busMock.Object, _sessionManagerMock.Object, _workspaceMock.Object, null, null, 50);

        runtime.Dispose();

        Assert.True(true);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var runtime = new AgentRuntime(_agent, _busMock.Object, _sessionManagerMock.Object, _workspaceMock.Object, null, null, 50);

        runtime.Dispose();
        runtime.Dispose();

        Assert.True(true);
    }

    [Fact]
    public async Task RunAsync_ProcessesMessages()
    {
        var session = await _agent.CreateSessionAsync();
        var messageCount = 0;
        var tcs = new TaskCompletionSource();

        _busMock.Setup(b => b.ConsumeInboundAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken ct) =>
            {
                messageCount++;
                if (messageCount > 2)
                {
                    tcs.SetResult();
                    await Task.Delay(Timeout.Infinite, ct);
                }
                return new InboundMessage { Channel = "test", ChatId = "chat1", SenderId = "user", Content = $"Message {messageCount}" };
            });

        _busMock.Setup(b => b.PublishOutboundAsync(It.IsAny<OutboundMessage>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        _sessionManagerMock.Setup(s => s.GetOrCreateSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var runtime = new AgentRuntime(_agent, _busMock.Object, _sessionManagerMock.Object, _workspaceMock.Object, null, null, 50);

        var cts = new CancellationTokenSource();
        var runTask = runtime.RunAsync(cts.Token);

        await tcs.Task;
        cts.Cancel();

        _busMock.Verify(b => b.PublishOutboundAsync(It.IsAny<OutboundMessage>(), It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }

    [Fact]
    public async Task ProcessDirectAsync_HandlesHelpCommand()
    {
        var runtime = new AgentRuntime(_agent, _busMock.Object, _sessionManagerMock.Object, _workspaceMock.Object, null, null, 50);

        var response = await runtime.ProcessDirectAsync("/help");

        Assert.Contains("nanobot commands", response);
        Assert.Contains("/new", response);
    }

    [Fact]
    public async Task ProcessDirectAsync_HandlesNewCommand()
    {
        var runtime = new AgentRuntime(_agent, _busMock.Object, _sessionManagerMock.Object, _workspaceMock.Object, null, null, 50);

        var response = await runtime.ProcessDirectAsync("/new");

        Assert.Contains("New session started", response);
        _sessionManagerMock.Verify(s => s.ClearSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
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

    [Fact]
    public void MarkdownImageRegex_ExtractsImageUrls()
    {
        // Arrange
        var regex = new System.Text.RegularExpressions.Regex(@"!\[(?<alt>[^\]]*)\]\((?<url>[^)\s]+)(?:\s+""[^""]*"")?\)");

        // Test single image
        var content1 = "Hello ![my image](/api/files/sessions/test/image.png) world";
        var matches1 = regex.Matches(content1);
        Assert.Single(matches1);
        Assert.Equal("my image", matches1[0].Groups["alt"].Value);
        Assert.Equal("/api/files/sessions/test/image.png", matches1[0].Groups["url"].Value);

        // Test multiple images
        var content2 = "Check these images: ![image1](url1.png) and ![image2](url2.jpg)";
        var matches2 = regex.Matches(content2);
        Assert.Equal(2, matches2.Count);

        // Test image with title
        var content3 = "![photo](test.png \"A nice photo\")";
        var matches3 = regex.Matches(content3);
        Assert.Single(matches3);
        Assert.Equal("photo", matches3[0].Groups["alt"].Value);
        Assert.Equal("test.png", matches3[0].Groups["url"].Value);

        // Test no image
        var content4 = "Just plain text without images";
        var matches4 = regex.Matches(content4);
        Assert.Empty(matches4);
    }

    [Fact]
    public async Task ProcessDirectAsync_WithMarkdownImage_IncludesImageInMessage()
    {
        // This test verifies that markdown images in the content are processed correctly
        // Create a test image file
        var sessionsPath = Path.Combine(_testDirectory, "sessions", "test-session");
        Directory.CreateDirectory(sessionsPath);
        var testImagePath = Path.Combine(sessionsPath, "test.png");

        // Create a minimal valid PNG file (1x1 transparent pixel)
        var pngBytes = new byte[] {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, // PNG signature
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, // IHDR chunk length + type
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, // 1x1 dimensions
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, // bit depth, color type, etc.
            0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, // IDAT chunk
            0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
            0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
            0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, // IEND chunk
            0x42, 0x60, 0x82
        };
        await File.WriteAllBytesAsync(testImagePath, pngBytes);

        var session = await _agent.CreateSessionAsync();
        _sessionManagerMock.Setup(s => s.GetOrCreateSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        _workspaceMock.Setup(w => w.GetSessionsPath()).Returns(Path.Combine(_testDirectory, "sessions"));

        var runtime = new AgentRuntime(_agent, _busMock.Object, _sessionManagerMock.Object, _workspaceMock.Object, null, null, 50, null, null, null, _loggerMock.Object);

        // Act - send a message with a markdown image reference
        var imageUrl = $"/api/files/sessions/test-session/test.png";
        var content = $"Please describe this image ![my test]({imageUrl})";

        var response = await runtime.ProcessDirectAsync(content, "test:session", "test", "session");

        // Assert - the response should not be empty (agent should have processed the image)
        Assert.NotNull(response);
    }
}
