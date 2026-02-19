using Microsoft.Extensions.AI;
using Moq;
using NanoBot.Core.Cron;
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

    [Fact]
    public void CreateCronTool_HasCorrectName()
    {
        var tool = CronTools.CreateCronTool(null, "whatsapp", "123456");

        Assert.Equal("cron", tool.Name);
    }

    [Fact]
    public void CreateCronTool_HasCorrectDescription()
    {
        var tool = CronTools.CreateCronTool(null, "whatsapp", "123456");

        Assert.Contains("Schedule reminders", tool.Description);
        Assert.Contains("add", tool.Description);
        Assert.Contains("list", tool.Description);
        Assert.Contains("remove", tool.Description);
    }

    [Fact]
    public async Task CronTools_AddJobMethod_ValidatesTimezone()
    {
        var mockCronService = new Mock<ICronService>();
        mockCronService.Setup(x => x.AddJob(It.IsAny<CronJobDefinition>()))
            .Returns(new CronJob { Id = "test", Name = "test", Message = "test", Schedule = new CronSchedule { Kind = CronScheduleKind.Cron }, Enabled = true });

        var tool = CronTools.CreateCronTool(mockCronService.Object, "whatsapp", "123456");
        var func = (AIFunction)tool;

        var result = await func.InvokeAsync(
            new AIFunctionArguments
            {
                ["action"] = "add",
                ["message"] = "Test",
                ["everySeconds"] = null,
                ["cronExpr"] = "0 0 9 * * *",
                ["tz"] = "Invalid/Timezone",
                ["at"] = null,
                ["jobId"] = null
            },
            CancellationToken.None);

        Assert.Contains("Unknown timezone", result?.ToString() ?? "");
    }

    [Fact]
    public async Task CronTools_AddJobMethod_TimezoneRequiresCronExpr()
    {
        var mockCronService = new Mock<ICronService>();
        mockCronService.Setup(x => x.AddJob(It.IsAny<CronJobDefinition>()))
            .Returns(new CronJob { Id = "test", Name = "test", Message = "test", Schedule = new CronSchedule { Kind = CronScheduleKind.Every }, Enabled = true });

        var tool = CronTools.CreateCronTool(mockCronService.Object, "whatsapp", "123456");
        var func = (AIFunction)tool;

        var result = await func.InvokeAsync(
            new AIFunctionArguments
            {
                ["action"] = "add",
                ["message"] = "Test",
                ["everySeconds"] = 60,
                ["cronExpr"] = null,
                ["tz"] = "America/New_York",
                ["at"] = null,
                ["jobId"] = null
            },
            CancellationToken.None);

        Assert.Contains("tz can only be used with cron_expr", result?.ToString() ?? "");
    }

    [Fact]
    public async Task CronTools_AddJobMethod_SetsDeliverTrue()
    {
        var mockCronService = new Mock<ICronService>();
        CronJobDefinition? capturedDefinition = null;
        mockCronService.Setup(x => x.AddJob(It.IsAny<CronJobDefinition>()))
            .Callback<CronJobDefinition>(d => capturedDefinition = d)
            .Returns(new CronJob { Id = "test", Name = "test", Message = "test", Schedule = new CronSchedule { Kind = CronScheduleKind.Cron }, Enabled = true });

        var tool = CronTools.CreateCronTool(mockCronService.Object, "whatsapp", "123456");
        var func = (AIFunction)tool;

        await func.InvokeAsync(
            new AIFunctionArguments
            {
                ["action"] = "add",
                ["message"] = "Test",
                ["everySeconds"] = null,
                ["cronExpr"] = "0 0 9 * * *",
                ["tz"] = null,
                ["at"] = null,
                ["jobId"] = null
            },
            CancellationToken.None);

        Assert.NotNull(capturedDefinition);
        Assert.True(capturedDefinition.Deliver);
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
