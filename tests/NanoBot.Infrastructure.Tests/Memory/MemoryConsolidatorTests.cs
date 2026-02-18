using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using NanoBot.Core.Memory;
using NanoBot.Core.Workspace;
using NanoBot.Infrastructure.Memory;
using Xunit;

namespace NanoBot.Infrastructure.Tests.Memory;

public class MemoryConsolidatorTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly Mock<IChatClient> _chatClientMock;
    private readonly Mock<IMemoryStore> _memoryStoreMock;
    private readonly Mock<IWorkspaceManager> _workspaceMock;
    private readonly Mock<ILogger<MemoryConsolidator>> _loggerMock;

    public MemoryConsolidatorTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"nanobot_consolidator_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
        Directory.CreateDirectory(Path.Combine(_testDirectory, "memory"));

        _chatClientMock = new Mock<IChatClient>();
        _memoryStoreMock = new Mock<IMemoryStore>();
        _workspaceMock = CreateWorkspaceMock(_testDirectory);
        _loggerMock = new Mock<ILogger<MemoryConsolidator>>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ConsolidateAsync_DoesNothing_WhenNoMessages()
    {
        var consolidator = new MemoryConsolidator(
            _chatClientMock.Object,
            _memoryStoreMock.Object,
            _workspaceMock.Object,
            logger: _loggerMock.Object);

        await consolidator.ConsolidateAsync([], 0);

        _chatClientMock.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConsolidateAsync_DoesNothing_WhenMessagesLessThanWindow()
    {
        var consolidator = new MemoryConsolidator(
            _chatClientMock.Object,
            _memoryStoreMock.Object,
            _workspaceMock.Object,
            memoryWindow: 50,
            logger: _loggerMock.Object);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello"),
            new(ChatRole.Assistant, "Hi there!")
        };

        await consolidator.ConsolidateAsync(messages, 0);

        _chatClientMock.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConsolidateAsync_DoesNothing_WhenNoNewMessagesToConsolidate()
    {
        var consolidator = new MemoryConsolidator(
            _chatClientMock.Object,
            _memoryStoreMock.Object,
            _workspaceMock.Object,
            memoryWindow: 4,
            logger: _loggerMock.Object);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello"),
            new(ChatRole.Assistant, "Hi there!"),
            new(ChatRole.User, "How are you?"),
            new(ChatRole.Assistant, "I'm good!")
        };

        await consolidator.ConsolidateAsync(messages, lastConsolidatedIndex: 4);

        _chatClientMock.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConsolidateAsync_CallsLLM_WhenMessagesNeedConsolidation()
    {
        var jsonResult = new JsonObject
        {
            ["history_entry"] = "[2024-01-15 10:30] User asked about weather.",
            ["memory_update"] = "User is interested in weather information."
        };

        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, jsonResult.ToJsonString()));

        _chatClientMock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        _memoryStoreMock.Setup(m => m.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("Existing memory");

        var consolidator = new MemoryConsolidator(
            _chatClientMock.Object,
            _memoryStoreMock.Object,
            _workspaceMock.Object,
            memoryWindow: 2,
            logger: _loggerMock.Object);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello"),
            new(ChatRole.Assistant, "Hi there!"),
            new(ChatRole.User, "What's the weather?"),
            new(ChatRole.Assistant, "I don't have access to weather data.")
        };

        await consolidator.ConsolidateAsync(messages, lastConsolidatedIndex: 0);

        _chatClientMock.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConsolidateAsync_UpdatesHistoryAndMemory()
    {
        var jsonResult = new JsonObject
        {
            ["history_entry"] = "[2024-01-15 10:30] User asked about weather.",
            ["memory_update"] = "User is interested in weather information."
        };

        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, jsonResult.ToJsonString()));

        _chatClientMock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        _memoryStoreMock.Setup(m => m.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("Existing memory");

        var consolidator = new MemoryConsolidator(
            _chatClientMock.Object,
            _memoryStoreMock.Object,
            _workspaceMock.Object,
            memoryWindow: 2,
            logger: _loggerMock.Object);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello"),
            new(ChatRole.Assistant, "Hi there!"),
            new(ChatRole.User, "What's the weather?"),
            new(ChatRole.Assistant, "I don't have access to weather data.")
        };

        await consolidator.ConsolidateAsync(messages, lastConsolidatedIndex: 0);

        _memoryStoreMock.Verify(m => m.AppendHistoryAsync(
            It.Is<string>(s => s.Contains("weather")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConsolidateAsync_HandlesEmptyLLMResponse()
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, ""));

        _chatClientMock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        _memoryStoreMock.Setup(m => m.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("");

        var consolidator = new MemoryConsolidator(
            _chatClientMock.Object,
            _memoryStoreMock.Object,
            _workspaceMock.Object,
            memoryWindow: 2,
            logger: _loggerMock.Object);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello"),
            new(ChatRole.Assistant, "Hi there!"),
            new(ChatRole.User, "What's the weather?"),
            new(ChatRole.Assistant, "I don't have access to weather data.")
        };

        await consolidator.ConsolidateAsync(messages, lastConsolidatedIndex: 0);

        _memoryStoreMock.Verify(m => m.AppendHistoryAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConsolidateAsync_HandlesInvalidJsonResponse()
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "This is not valid JSON"));

        _chatClientMock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        _memoryStoreMock.Setup(m => m.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("");

        var consolidator = new MemoryConsolidator(
            _chatClientMock.Object,
            _memoryStoreMock.Object,
            _workspaceMock.Object,
            memoryWindow: 2,
            logger: _loggerMock.Object);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello"),
            new(ChatRole.Assistant, "Hi there!"),
            new(ChatRole.User, "What's the weather?"),
            new(ChatRole.Assistant, "I don't have access to weather data.")
        };

        await consolidator.ConsolidateAsync(messages, lastConsolidatedIndex: 0);

        _memoryStoreMock.Verify(m => m.AppendHistoryAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConsolidateAsync_ArchiveAll_ProcessesAllMessages()
    {
        var jsonResult = new JsonObject
        {
            ["history_entry"] = "[2024-01-15 10:30] Conversation archived.",
            ["memory_update"] = "Archived conversation."
        };

        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, jsonResult.ToJsonString()));

        _chatClientMock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        _memoryStoreMock.Setup(m => m.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("");

        var consolidator = new MemoryConsolidator(
            _chatClientMock.Object,
            _memoryStoreMock.Object,
            _workspaceMock.Object,
            memoryWindow: 50,
            logger: _loggerMock.Object);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello"),
            new(ChatRole.Assistant, "Hi there!")
        };

        await consolidator.ConsolidateAsync(messages, lastConsolidatedIndex: 0, archiveAll: true);

        _chatClientMock.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConsolidateAsync_HandlesLLMException()
    {
        _chatClientMock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("LLM error"));

        _memoryStoreMock.Setup(m => m.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("");

        var consolidator = new MemoryConsolidator(
            _chatClientMock.Object,
            _memoryStoreMock.Object,
            _workspaceMock.Object,
            memoryWindow: 2,
            logger: _loggerMock.Object);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello"),
            new(ChatRole.Assistant, "Hi there!"),
            new(ChatRole.User, "What's the weather?"),
            new(ChatRole.Assistant, "I don't have access to weather data.")
        };

        await consolidator.ConsolidateAsync(messages, lastConsolidatedIndex: 0);

        _memoryStoreMock.Verify(m => m.AppendHistoryAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConsolidateAsync_HandlesMarkdownCodeFences()
    {
        var jsonResult = new JsonObject
        {
            ["history_entry"] = "Test entry",
            ["memory_update"] = "Test update"
        };

        var responseWithFences = new ChatResponse(new ChatMessage(ChatRole.Assistant, 
            $"```json\n{jsonResult.ToJsonString()}\n```"));

        _chatClientMock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseWithFences);

        _memoryStoreMock.Setup(m => m.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("");

        var consolidator = new MemoryConsolidator(
            _chatClientMock.Object,
            _memoryStoreMock.Object,
            _workspaceMock.Object,
            memoryWindow: 2,
            logger: _loggerMock.Object);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello"),
            new(ChatRole.Assistant, "Hi there!"),
            new(ChatRole.User, "What's the weather?"),
            new(ChatRole.Assistant, "I don't have access to weather data.")
        };

        await consolidator.ConsolidateAsync(messages, lastConsolidatedIndex: 0);

        _memoryStoreMock.Verify(m => m.AppendHistoryAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Mock<IWorkspaceManager> CreateWorkspaceMock(string testDir)
    {
        var mock = new Mock<IWorkspaceManager>();
        mock.Setup(w => w.GetMemoryFile()).Returns(Path.Combine(testDir, "memory", "MEMORY.md"));
        mock.Setup(w => w.GetHistoryFile()).Returns(Path.Combine(testDir, "memory", "HISTORY.md"));
        mock.Setup(w => w.GetWorkspacePath()).Returns(testDir);
        return mock;
    }
}
