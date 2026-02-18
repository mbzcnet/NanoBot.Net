using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using NanoBot.Agent.Tools;
using NanoBot.Core.Workspace;
using Xunit;

namespace NanoBot.Agent.Tests.Tools;

public class SpawnToolTests
{
    [Fact]
    public void CreateSpawnTool_ReturnsValidAITool()
    {
        var chatClientMock = CreateChatClientMock();
        var workspaceMock = CreateWorkspaceMock();

        var tool = SpawnTool.CreateSpawnTool(chatClientMock.Object, workspaceMock.Object);

        Assert.NotNull(tool);
        var function = tool as AIFunction;
        Assert.NotNull(function);
        Assert.Equal("spawn", function.Name);
        Assert.Contains("sub-agent", function.Description);
    }

    [Fact]
    public void CreateSpawnTool_ThrowsOnNullChatClient()
    {
        var workspaceMock = CreateWorkspaceMock();

        Assert.Throws<ArgumentNullException>(() =>
            SpawnTool.CreateSpawnTool(null!, workspaceMock.Object));
    }

    [Fact]
    public void CreateSpawnTool_ThrowsOnNullWorkspace()
    {
        var chatClientMock = CreateChatClientMock();

        Assert.Throws<ArgumentNullException>(() =>
            SpawnTool.CreateSpawnTool(chatClientMock.Object, null!));
    }

    [Fact]
    public void CreateSubAgentAsFunction_ReturnsValidAIFunction()
    {
        var chatClientMock = CreateChatClientMock();
        var workspaceMock = CreateWorkspaceMock();

        var function = SpawnTool.CreateSubAgentAsFunction(
            chatClientMock.Object,
            workspaceMock.Object,
            "Test task",
            "test_agent");

        Assert.NotNull(function);
        Assert.Equal("test_agent", function.Name);
        Assert.Contains("Test task", function.Description);
    }

    [Fact]
    public void CreateSubAgentAsFunction_GeneratesName_WhenNotProvided()
    {
        var chatClientMock = CreateChatClientMock();
        var workspaceMock = CreateWorkspaceMock();

        var function = SpawnTool.CreateSubAgentAsFunction(
            chatClientMock.Object,
            workspaceMock.Object,
            "Test task");

        Assert.NotNull(function);
        Assert.StartsWith("subagent_", function.Name);
    }

    private static Mock<IChatClient> CreateChatClientMock()
    {
        var mock = new Mock<IChatClient>();
        var metadata = new ChatClientMetadata("test");
        mock.Setup(c => c.GetService(typeof(ChatClientMetadata), null))
            .Returns(metadata);
        return mock;
    }

    private static Mock<IWorkspaceManager> CreateWorkspaceMock()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"nanobot_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var mock = new Mock<IWorkspaceManager>();
        mock.Setup(w => w.GetWorkspacePath()).Returns(tempDir);
        mock.Setup(w => w.GetSkillsPath()).Returns(Path.Combine(tempDir, "skills"));
        return mock;
    }
}
