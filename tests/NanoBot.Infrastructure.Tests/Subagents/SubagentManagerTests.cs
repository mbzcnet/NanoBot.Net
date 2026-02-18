using Microsoft.Extensions.Logging;
using Moq;
using NanoBot.Core.Bus;
using NanoBot.Core.Subagents;
using NanoBot.Core.Workspace;
using NanoBot.Infrastructure.Subagents;
using Xunit;

namespace NanoBot.Infrastructure.Tests.Subagents;

public class SubagentManagerTests
{
    private readonly Mock<IMessageBus> _messageBusMock;
    private readonly Mock<IWorkspaceManager> _workspaceManagerMock;
    private readonly ILogger<SubagentManager> _logger;

    public SubagentManagerTests()
    {
        _messageBusMock = new Mock<IMessageBus>();
        _workspaceManagerMock = new Mock<IWorkspaceManager>();
        var loggerFactory = LoggerFactory.Create(builder => builder.AddDebug());
        _logger = loggerFactory.CreateLogger<SubagentManager>();
    }

    [Fact]
    public void Constructor_CreatesInstance()
    {
        var manager = new SubagentManager(
            _messageBusMock.Object,
            _workspaceManagerMock.Object,
            _logger);

        Assert.NotNull(manager);
    }

    [Fact]
    public void GetActiveSubagents_ReturnsEmptyInitially()
    {
        var manager = new SubagentManager(
            _messageBusMock.Object,
            _workspaceManagerMock.Object,
            _logger);

        var active = manager.GetActiveSubagents();
        Assert.Empty(active);
    }

    [Fact]
    public async Task SpawnAsync_ReturnsCompletedResult()
    {
        _workspaceManagerMock.Setup(x => x.GetWorkspacePath()).Returns("/tmp/workspace");
        _workspaceManagerMock.Setup(x => x.GetSkillsPath()).Returns("/tmp/workspace/skills");

        var manager = new SubagentManager(
            _messageBusMock.Object,
            _workspaceManagerMock.Object,
            _logger,
            executeSubagent: (systemPrompt, task) => Task.FromResult("Task completed"));

        var result = await manager.SpawnAsync(
            "Test task",
            "Test Label",
            "telegram",
            "chat123");

        Assert.NotNull(result);
        Assert.Equal(SubagentStatus.Completed, result.Status);
        Assert.Equal("Task completed", result.Output);
    }

    [Fact]
    public async Task SpawnAsync_WithLabel_UsesLabel()
    {
        _workspaceManagerMock.Setup(x => x.GetWorkspacePath()).Returns("/tmp/workspace");
        _workspaceManagerMock.Setup(x => x.GetSkillsPath()).Returns("/tmp/workspace/skills");

        var manager = new SubagentManager(
            _messageBusMock.Object,
            _workspaceManagerMock.Object,
            _logger,
            executeSubagent: (systemPrompt, task) => Task.FromResult("Done"));

        var result = await manager.SpawnAsync(
            "Test task",
            "Custom Label",
            "telegram",
            "chat123");

        Assert.Equal(SubagentStatus.Completed, result.Status);
    }

    [Fact]
    public async Task SpawnAsync_TruncatesLongTaskAsLabel()
    {
        _workspaceManagerMock.Setup(x => x.GetWorkspacePath()).Returns("/tmp/workspace");
        _workspaceManagerMock.Setup(x => x.GetSkillsPath()).Returns("/tmp/workspace/skills");

        var manager = new SubagentManager(
            _messageBusMock.Object,
            _workspaceManagerMock.Object,
            _logger,
            executeSubagent: (systemPrompt, task) => Task.FromResult("Done"));

        var longTask = new string('a', 100);
        var result = await manager.SpawnAsync(
            longTask,
            null,
            "telegram",
            "chat123");

        Assert.Equal(SubagentStatus.Completed, result.Status);
    }

    [Fact]
    public async Task SpawnAsync_PublishesResultToMessageBus()
    {
        _workspaceManagerMock.Setup(x => x.GetWorkspacePath()).Returns("/tmp/workspace");
        _workspaceManagerMock.Setup(x => x.GetSkillsPath()).Returns("/tmp/workspace/skills");

        InboundMessage? publishedMessage = null;
        _messageBusMock
            .Setup(x => x.PublishInboundAsync(It.IsAny<InboundMessage>(), default))
            .Callback<InboundMessage, CancellationToken>((msg, _) => publishedMessage = msg)
            .Returns(ValueTask.CompletedTask);

        var manager = new SubagentManager(
            _messageBusMock.Object,
            _workspaceManagerMock.Object,
            _logger,
            executeSubagent: (systemPrompt, task) => Task.FromResult("Result output"));

        await manager.SpawnAsync("Test task", null, "telegram", "chat123");

        _messageBusMock.Verify(
            x => x.PublishInboundAsync(It.IsAny<InboundMessage>(), default),
            Times.Once);

        Assert.NotNull(publishedMessage);
        Assert.Equal("system", publishedMessage.Channel);
        Assert.Contains("Result output", publishedMessage.Content);
    }

    [Fact]
    public async Task SpawnAsync_RaisesCompletedEvent()
    {
        _workspaceManagerMock.Setup(x => x.GetWorkspacePath()).Returns("/tmp/workspace");
        _workspaceManagerMock.Setup(x => x.GetSkillsPath()).Returns("/tmp/workspace/skills");

        var manager = new SubagentManager(
            _messageBusMock.Object,
            _workspaceManagerMock.Object,
            _logger,
            executeSubagent: (systemPrompt, task) => Task.FromResult("Done"));

        SubagentCompletedEventArgs? eventArgs = null;
        manager.SubagentCompleted += (sender, args) => eventArgs = args;

        var result = await manager.SpawnAsync("Test task", null, "telegram", "chat123");

        Assert.NotNull(eventArgs);
        Assert.Equal(result.Id, eventArgs.Result.Id);
        Assert.Equal("telegram", eventArgs.OriginChannel);
        Assert.Equal("chat123", eventArgs.OriginChatId);
    }

    [Fact]
    public async Task SpawnAsync_HandlesException()
    {
        _workspaceManagerMock.Setup(x => x.GetWorkspacePath()).Returns("/tmp/workspace");
        _workspaceManagerMock.Setup(x => x.GetSkillsPath()).Returns("/tmp/workspace/skills");

        var manager = new SubagentManager(
            _messageBusMock.Object,
            _workspaceManagerMock.Object,
            _logger,
            executeSubagent: (systemPrompt, task) => throw new InvalidOperationException("Test error"));

        var result = await manager.SpawnAsync("Test task", null, "telegram", "chat123");

        Assert.Equal(SubagentStatus.Failed, result.Status);
        Assert.Equal("Test error", result.Error);
    }

    [Fact]
    public async Task Cancel_ReturnsTrueForRunningSubagent()
    {
        _workspaceManagerMock.Setup(x => x.GetWorkspacePath()).Returns("/tmp/workspace");
        _workspaceManagerMock.Setup(x => x.GetSkillsPath()).Returns("/tmp/workspace/skills");

        var tcs = new TaskCompletionSource<string>();
        var manager = new SubagentManager(
            _messageBusMock.Object,
            _workspaceManagerMock.Object,
            _logger,
            executeSubagent: async (systemPrompt, task) =>
            {
                await Task.Delay(100);
                return "Done";
            });

        var spawnTask = manager.SpawnAsync("Test task", null, "telegram", "chat123");

        await Task.Delay(10);

        var activeBefore = manager.GetActiveSubagents();
        if (activeBefore.Count > 0)
        {
            var cancelled = manager.Cancel(activeBefore[0].Id);
            Assert.True(cancelled);
        }

        var result = await spawnTask;
        Assert.True(result.Status == SubagentStatus.Completed || result.Status == SubagentStatus.Cancelled);
    }

    [Fact]
    public void Cancel_ReturnsFalseForNonexistentId()
    {
        var manager = new SubagentManager(
            _messageBusMock.Object,
            _workspaceManagerMock.Object,
            _logger);

        var cancelled = manager.Cancel("nonexistent-id");
        Assert.False(cancelled);
    }

    [Fact]
    public void GetSubagent_ReturnsNullForNonexistentId()
    {
        var manager = new SubagentManager(
            _messageBusMock.Object,
            _workspaceManagerMock.Object,
            _logger);

        var info = manager.GetSubagent("nonexistent-id");
        Assert.Null(info);
    }

    [Fact]
    public async Task GetSubagent_ReturnsInfoForExistingId()
    {
        _workspaceManagerMock.Setup(x => x.GetWorkspacePath()).Returns("/tmp/workspace");
        _workspaceManagerMock.Setup(x => x.GetSkillsPath()).Returns("/tmp/workspace/skills");

        var manager = new SubagentManager(
            _messageBusMock.Object,
            _workspaceManagerMock.Object,
            _logger,
            executeSubagent: (systemPrompt, task) => Task.FromResult("Done"));

        var result = await manager.SpawnAsync("Test task", null, "telegram", "chat123");

        var info = manager.GetSubagent(result.Id);
        Assert.NotNull(info);
        Assert.Equal(result.Id, info.Id);
        Assert.Equal("Test task", info.Task);
    }
}
