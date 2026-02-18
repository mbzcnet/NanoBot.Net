using Microsoft.Extensions.AI;
using NanoBot.Tools.BuiltIn;
using Xunit;

namespace NanoBot.Tools.Tests;

public class FileToolsTests
{
    [Fact]
    public void CreateReadFileTool_ReturnsAITool()
    {
        var tool = FileTools.CreateReadFileTool();

        Assert.NotNull(tool);
        Assert.IsAssignableFrom<AIFunction>(tool);
    }

    [Fact]
    public void CreateWriteFileTool_ReturnsAITool()
    {
        var tool = FileTools.CreateWriteFileTool();

        Assert.NotNull(tool);
        Assert.IsAssignableFrom<AIFunction>(tool);
    }

    [Fact]
    public void CreateEditFileTool_ReturnsAITool()
    {
        var tool = FileTools.CreateEditFileTool();

        Assert.NotNull(tool);
        Assert.IsAssignableFrom<AIFunction>(tool);
    }

    [Fact]
    public void CreateListDirTool_ReturnsAITool()
    {
        var tool = FileTools.CreateListDirTool();

        Assert.NotNull(tool);
        Assert.IsAssignableFrom<AIFunction>(tool);
    }
}

public class ShellToolsTests
{
    [Fact]
    public void CreateExecTool_ReturnsAITool()
    {
        var tool = ShellTools.CreateExecTool();

        Assert.NotNull(tool);
        Assert.IsAssignableFrom<AIFunction>(tool);
    }

    [Fact]
    public void CreateExecTool_WithBlockedCommands_ReturnsAITool()
    {
        var blockedCommands = new[] { "rm", "del" };
        var tool = ShellTools.CreateExecTool(blockedCommands);

        Assert.NotNull(tool);
        Assert.IsAssignableFrom<AIFunction>(tool);
    }
}

public class WebToolsTests
{
    [Fact]
    public void CreateWebSearchTool_ReturnsAITool()
    {
        var tool = WebTools.CreateWebSearchTool();

        Assert.NotNull(tool);
        Assert.IsAssignableFrom<AIFunction>(tool);
    }

    [Fact]
    public void CreateWebFetchTool_ReturnsAITool()
    {
        var tool = WebTools.CreateWebFetchTool();

        Assert.NotNull(tool);
        Assert.IsAssignableFrom<AIFunction>(tool);
    }
}

public class MessageToolsTests
{
    [Fact]
    public void CreateMessageTool_ReturnsAITool()
    {
        var tool = MessageTools.CreateMessageTool(null);

        Assert.NotNull(tool);
        Assert.IsAssignableFrom<AIFunction>(tool);
    }
}

public class CronToolsTests
{
    [Fact]
    public void CreateCronTool_ReturnsAITool()
    {
        var tool = CronTools.CreateCronTool(null);

        Assert.NotNull(tool);
        Assert.IsAssignableFrom<AIFunction>(tool);
    }
}

public class SpawnToolsTests
{
    [Fact]
    public void CreateSpawnTool_ReturnsAITool()
    {
        var tool = SpawnTools.CreateSpawnTool(null);

        Assert.NotNull(tool);
        Assert.IsAssignableFrom<AIFunction>(tool);
    }
}
