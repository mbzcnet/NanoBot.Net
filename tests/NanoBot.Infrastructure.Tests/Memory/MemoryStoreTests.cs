using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using NanoBot.Core.Configuration;
using NanoBot.Core.Workspace;
using NanoBot.Infrastructure.Memory;
using Xunit;

namespace NanoBot.Infrastructure.Tests.Memory;

public class MemoryStoreTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly Mock<IWorkspaceManager> _mockWorkspace;
    private readonly MemoryConfig _config;
    private readonly Mock<ILogger<MemoryStore>> _mockLogger;

    public MemoryStoreTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"nanobot_memory_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
        Directory.CreateDirectory(Path.Combine(_testDirectory, "memory"));

        _mockWorkspace = new Mock<IWorkspaceManager>();
        _mockWorkspace.Setup(w => w.GetMemoryFile())
            .Returns(Path.Combine(_testDirectory, "memory", "MEMORY.md"));

        _config = new MemoryConfig
        {
            Enabled = true,
            MaxHistoryEntries = 100
        };

        _mockLogger = new Mock<ILogger<MemoryStore>>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_ReturnsEmpty_WhenFileDoesNotExist()
    {
        var store = new MemoryStore(_mockWorkspace.Object, _config, _mockLogger.Object);

        var result = await store.LoadAsync();

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task LoadAsync_ReturnsContent_WhenFileExists()
    {
        var memoryPath = Path.Combine(_testDirectory, "memory", "MEMORY.md");
        var expectedContent = "# Memory\n\nTest content";
        await File.WriteAllTextAsync(memoryPath, expectedContent);

        var store = new MemoryStore(_mockWorkspace.Object, _config, _mockLogger.Object);

        var result = await store.LoadAsync();

        Assert.Equal(expectedContent, result);
    }

    [Fact]
    public async Task LoadAsync_ReturnsCachedContent_OnSecondCall()
    {
        var memoryPath = Path.Combine(_testDirectory, "memory", "MEMORY.md");
        var expectedContent = "# Memory\n\nTest content";
        await File.WriteAllTextAsync(memoryPath, expectedContent);

        var store = new MemoryStore(_mockWorkspace.Object, _config, _mockLogger.Object);

        var result1 = await store.LoadAsync();
        var result2 = await store.LoadAsync();

        Assert.Equal(expectedContent, result1);
        Assert.Equal(expectedContent, result2);
    }

    [Fact]
    public async Task UpdateAsync_DoesNotUpdate_WhenDisabled()
    {
        var disabledConfig = new MemoryConfig { Enabled = false };
        var store = new MemoryStore(_mockWorkspace.Object, disabledConfig, _mockLogger.Object);

        var requestMessages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello")
        };
        var responseMessages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, "Hi there!")
        };

        await store.UpdateAsync(requestMessages, responseMessages);

        var memoryPath = Path.Combine(_testDirectory, "memory", "MEMORY.md");
        Assert.False(File.Exists(memoryPath));
    }

    [Fact]
    public async Task UpdateAsync_CreatesMemoryFile_WhenNotExists()
    {
        var store = new MemoryStore(_mockWorkspace.Object, _config, _mockLogger.Object);

        var requestMessages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello")
        };
        var responseMessages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, "Hi there!")
        };

        await store.UpdateAsync(requestMessages, responseMessages);

        var memoryPath = Path.Combine(_testDirectory, "memory", "MEMORY.md");
        Assert.True(File.Exists(memoryPath));

        var content = await File.ReadAllTextAsync(memoryPath);
        Assert.Contains("Hello", content);
        Assert.Contains("Hi there!", content);
    }

    [Fact]
    public async Task GetMemoryContext_ReturnsEmpty_WhenNoMemory()
    {
        var store = new MemoryStore(_mockWorkspace.Object, _config, _mockLogger.Object);

        var context = store.GetMemoryContext();

        Assert.Equal(string.Empty, context);
    }

    [Fact]
    public async Task GetMemoryContext_ReturnsFormattedContext_WhenMemoryExists()
    {
        var memoryPath = Path.Combine(_testDirectory, "memory", "MEMORY.md");
        await File.WriteAllTextAsync(memoryPath, "Test memory content");

        var store = new MemoryStore(_mockWorkspace.Object, _config, _mockLogger.Object);
        await store.LoadAsync();

        var context = store.GetMemoryContext();

        Assert.StartsWith("## Long-term Memory", context);
        Assert.Contains("Test memory content", context);
    }

    [Fact]
    public async Task UpdateAsync_TruncatesLongMessages()
    {
        var store = new MemoryStore(_mockWorkspace.Object, _config, _mockLogger.Object);

        var longMessage = new string('x', 600);
        var requestMessages = new List<ChatMessage>
        {
            new(ChatRole.User, longMessage)
        };
        var responseMessages = new List<ChatMessage>();

        await store.UpdateAsync(requestMessages, responseMessages);

        var memoryPath = Path.Combine(_testDirectory, "memory", "MEMORY.md");
        var content = await File.ReadAllTextAsync(memoryPath);
        Assert.Contains("...", content);
    }
}
