using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NanoBot.Core.Cron;
using NanoBot.Core.Tools.Browser;
using NanoBot.Core.Workspace;
using NanoBot.Infrastructure.Browser;
using NanoBot.Tools.BuiltIn;
using NanoBot.Tools.Tests.Attributes;
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

public class BrowserToolsTests
{
    private static bool EnsureBrowserIntegrationEnabled()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("NANOBOT_BROWSER_INTEGRATION"),
            "1",
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateBrowserContentTool_ReturnsAITool()
    {
        var tool = BrowserTools.CreateBrowserContentTool(null);

        Assert.NotNull(tool);
        Assert.Equal("browser_content", tool.Name);
        Assert.IsAssignableFrom<AIFunction>(tool);
    }

    [Fact]
    public void CreateBrowserInteractTool_ReturnsAITool()
    {
        var tool = BrowserTools.CreateBrowserInteractTool(null);

        Assert.NotNull(tool);
        Assert.Equal("browser_interact", tool.Name);
        Assert.IsAssignableFrom<AIFunction>(tool);
    }

    [Fact]
    public void CreateBrowserOpenTool_ReturnsAITool()
    {
        var tool = BrowserTools.CreateBrowserOpenTool(null);

        Assert.NotNull(tool);
        Assert.Equal("browser_open", tool.Name);
        Assert.IsAssignableFrom<AIFunction>(tool);
    }

    [Fact]
    public void CreateBrowserSnapshotTool_ReturnsAITool()
    {
        var tool = BrowserTools.CreateBrowserSnapshotTool(null);

        Assert.NotNull(tool);
        Assert.Equal("browser_snapshot", tool.Name);
        Assert.IsAssignableFrom<AIFunction>(tool);
    }

    [Fact]
    public void CreateBrowserScreenshotTool_ReturnsAITool()
    {
        var tool = BrowserTools.CreateBrowserScreenshotTool(null);

        Assert.NotNull(tool);
        Assert.Equal("browser_screenshot", tool.Name);
        Assert.IsAssignableFrom<AIFunction>(tool);
    }

    [Fact]
    public void CreateBrowserTabsTool_ReturnsAITool()
    {
        var tool = BrowserTools.CreateBrowserTabsTool(null);

        Assert.NotNull(tool);
        Assert.Equal("browser_tabs", tool.Name);
        Assert.IsAssignableFrom<AIFunction>(tool);
    }

    [Fact]
    public void CreateBrowserNavigateTool_ReturnsAITool()
    {
        var tool = BrowserTools.CreateBrowserNavigateTool(null);

        Assert.NotNull(tool);
        Assert.Equal("browser_navigate", tool.Name);
        Assert.IsAssignableFrom<AIFunction>(tool);
    }

    [Fact]
    public void CreateBrowserCloseTool_ReturnsAITool()
    {
        var tool = BrowserTools.CreateBrowserCloseTool(null);

        Assert.NotNull(tool);
        Assert.Equal("browser_close", tool.Name);
        Assert.IsAssignableFrom<AIFunction>(tool);
    }

    [Fact]
    public async Task BrowserTools_Content_DelegatesToService()
    {
        var mockService = new Mock<IBrowserService>();
        mockService
            .Setup(x => x.GetContentAsync("t1", "main", 2000, "nanobot", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BrowserToolResponse
            {
                Ok = true,
                Action = "content",
                Profile = "nanobot",
                TargetId = "t1",
                Content = "headline"
            });

        var tool = BrowserTools.CreateBrowserContentTool(mockService.Object);
        var func = (AIFunction)tool;

        var result = await func.InvokeAsync(
            new AIFunctionArguments
            {
                ["targetId"] = "t1",
                ["selector"] = "main",
                ["maxChars"] = 2000,
                ["profile"] = "nanobot"
            },
            CancellationToken.None);

        var resultText = result?.ToString() ?? string.Empty;
        Assert.Contains("content", resultText);
        Assert.Contains("headline", resultText);
        mockService.Verify(x => x.GetContentAsync("t1", "main", 2000, "nanobot", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ToolProvider_DefaultTools_ContainsBrowserTools()
    {
        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();

        var tools = await ToolProvider.CreateDefaultToolsAsync(serviceProvider);

        Assert.Contains(tools, t => t.Name == "browser_open");
        Assert.Contains(tools, t => t.Name == "browser_snapshot");
        Assert.Contains(tools, t => t.Name == "browser_interact");
        Assert.Contains(tools, t => t.Name == "browser_content");
    }

    [SkipIfPlaywrightNotInstalled]
    public async Task BrowserService_StartOpenContentStop_UsesRealPlaywright()
    {
        var workspaceMock = new Mock<IWorkspaceManager>();
        using var browserService = new BrowserService(workspaceMock.Object);

        var start = await browserService.StartAsync("nanobot");
        Assert.True(start.Ok);

        var html = "<html><body><main><article>latest news: test headline</article></main><div id='loading'>loading...</div><button>Read more</button><script>setTimeout(() => { const el = document.getElementById('loading'); if (el) el.remove(); }, 150);</script></body></html>";
        var targetUrl = "data:text/html," + Uri.EscapeDataString(html);

        var opened = await browserService.OpenTabAsync(targetUrl, "nanobot");
        Assert.True(opened.Ok);
        Assert.False(string.IsNullOrWhiteSpace(opened.TargetId));

        var targetId = opened.TargetId!;

        var waitResult = await browserService.ExecuteActionAsync(
            new BrowserActionRequest { Kind = "wait", TimeoutMs = 1500, LoadState = "load", Text = "latest news" },
            targetId,
            "nanobot");
        Assert.True(waitResult.Ok);

        var waitTextGoneResult = await browserService.ExecuteActionAsync(
            new BrowserActionRequest { Kind = "wait", TimeoutMs = 1500, TextGone = "loading..." },
            targetId,
            "nanobot");
        Assert.True(waitTextGoneResult.Ok);

        var snapshotResult = await browserService.GetSnapshotAsync(targetId, "ai", "nanobot");
        Assert.True(snapshotResult.Ok);

        var hoverRef = snapshotResult.Refs?.Keys.FirstOrDefault();
        Assert.False(string.IsNullOrWhiteSpace(hoverRef));

        var hoverResult = await browserService.ExecuteActionAsync(
            new BrowserActionRequest { Kind = "hover", Ref = hoverRef, TimeoutMs = 1200 },
            targetId,
            "nanobot");
        Assert.True(hoverResult.Ok);

        var pressResult = await browserService.ExecuteActionAsync(
            new BrowserActionRequest { Kind = "press", Key = "Tab" },
            targetId,
            "nanobot");
        Assert.True(pressResult.Ok);

        var contentResult = await browserService.GetContentAsync(targetId, "main", 2000, "nanobot");
        Assert.True(contentResult.Ok);
        Assert.Contains("latest news", contentResult.Content ?? string.Empty);

        var tabs = await browserService.GetTabsAsync("nanobot");
        Assert.Contains(tabs, t => t.TargetId == targetId);

        var stopped = await browserService.StopAsync("nanobot");
        Assert.True(stopped.Ok);
    }

    [SkipIfPlaywrightNotInstalled]
    public async Task BrowserService_BaiduSnapshot_CanSaveScreenshotToSessionFolder()
    {
        if (!EnsureBrowserIntegrationEnabled()) return;
        var keepArtifacts = string.Equals(
            Environment.GetEnvironmentVariable("NANOBOT_BROWSER_KEEP_ARTIFACTS"),
            "1",
            StringComparison.OrdinalIgnoreCase);

        var workspaceRoot = Path.Combine(Path.GetTempPath(), "nanobot-browser-tests", Guid.NewGuid().ToString("N"));
        var sessionsPath = Path.Combine(workspaceRoot, "sessions");
        Directory.CreateDirectory(sessionsPath);

        var workspaceMock = new Mock<IWorkspaceManager>();
        workspaceMock.Setup(x => x.GetWorkspacePath()).Returns(workspaceRoot);
        workspaceMock.Setup(x => x.GetSessionsPath()).Returns(sessionsPath);
        workspaceMock.Setup(x => x.EnsureDirectory(It.IsAny<string>()))
            .Callback<string>(path => _ = Directory.CreateDirectory(path));

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        var logger = loggerFactory.CreateLogger<BrowserService>();

        try
        {
            using var browserService = new BrowserService(workspaceMock.Object, logger);

            var start = await browserService.StartAsync("nanobot");
            Assert.True(start.Ok);

            var open = await browserService.OpenTabAsync("https://www.baidu.com", "nanobot");
            Assert.True(open.Ok);
            Assert.False(string.IsNullOrWhiteSpace(open.TargetId));

            var response = await browserService.CaptureSnapshotAsync(
                open.TargetId!,
                "ai",
                "nanobot",
                "webui:baidu-snapshot-test");

            Assert.True(response.Ok);
            Assert.False(string.IsNullOrWhiteSpace(response.Snapshot));
            Assert.False(string.IsNullOrWhiteSpace(response.ImagePath));

            var localPath = Path.Combine(sessionsPath, response.ImagePath!.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(localPath), $"Snapshot file not found: {localPath}");

            Console.WriteLine($"Snapshot image relative path: {response.ImagePath}");
            Console.WriteLine($"Snapshot image local path: {localPath}");
            Console.WriteLine($"Snapshot image url: /api/files/sessions/{response.ImagePath.Replace('\\', '/')}");

            var stop = await browserService.StopAsync("nanobot");
            Assert.True(stop.Ok);
        }
        finally
        {
            if (!keepArtifacts && Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, true);
            }
        }
    }

    [SkipIfPlaywrightNotInstalled]
    public async Task BrowserService_SnapshotWithoutSessionKey_UsesFallbackAndSavesScreenshot()
    {
        if (!EnsureBrowserIntegrationEnabled()) return;

        var workspaceRoot = Path.Combine(Path.GetTempPath(), "nanobot-browser-tests", Guid.NewGuid().ToString("N"));
        var sessionsPath = Path.Combine(workspaceRoot, "sessions");
        Directory.CreateDirectory(sessionsPath);

        var workspaceMock = new Mock<IWorkspaceManager>();
        workspaceMock.Setup(x => x.GetWorkspacePath()).Returns(workspaceRoot);
        workspaceMock.Setup(x => x.GetSessionsPath()).Returns(sessionsPath);
        workspaceMock.Setup(x => x.EnsureDirectory(It.IsAny<string>()))
            .Callback<string>(path => _ = Directory.CreateDirectory(path));

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        var logger = loggerFactory.CreateLogger<BrowserService>();

        try
        {
            using var browserService = new BrowserService(workspaceMock.Object, logger);

            var start = await browserService.StartAsync("nanobot");
            Assert.True(start.Ok);

            var html = "<html><body><main><h1>fallback snapshot</h1></main></body></html>";
            var targetUrl = "data:text/html," + Uri.EscapeDataString(html);
            var open = await browserService.OpenTabAsync(targetUrl, "nanobot");
            Assert.True(open.Ok);

            var response = await browserService.CaptureSnapshotAsync(open.TargetId!, "ai", "nanobot", null);
            Assert.True(response.Ok);
            Assert.False(string.IsNullOrWhiteSpace(response.ImagePath));
            Assert.StartsWith("fallback_nanobot/", response.ImagePath, StringComparison.Ordinal);

            var localPath = Path.Combine(sessionsPath, response.ImagePath!.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(localPath), $"Snapshot file not found: {localPath}");
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, true);
            }
        }
    }
}

public class ShellToolsTests
{
    [Fact]
    public void CreateExecTool_ReturnsAITool()
    {
        var tool = ShellTools.CreateExecTool((ShellToolOptions?)null);

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
    public void CreateWebPageTool_ReturnsAITool()
    {
        var tool = WebTools.CreateWebPageTool();

        Assert.NotNull(tool);
        Assert.Equal("web_page", tool.Name);
        Assert.IsAssignableFrom<AIFunction>(tool);
    }

    [Fact]
    public void CreateWebPageTool_HasCorrectDescription()
    {
        var tool = WebTools.CreateWebPageTool();

        Assert.Contains("search", tool.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fetch", tool.Description, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public async Task CronTools_ListJobs_ShowsEmptyMessage()
    {
        var mockCronService = new Mock<ICronService>();
        mockCronService.Setup(x => x.ListJobs()).Returns(new List<CronJob>());

        var tool = CronTools.CreateCronTool(mockCronService.Object, "whatsapp", "123456");
        var func = (AIFunction)tool;

        var result = await func.InvokeAsync(
            new AIFunctionArguments
            {
                ["action"] = "list",
                ["message"] = null,
                ["everySeconds"] = null,
                ["cronExpr"] = null,
                ["tz"] = null,
                ["at"] = null,
                ["jobId"] = null
            },
            CancellationToken.None);

        Assert.Contains("No scheduled jobs", result?.ToString() ?? "");
    }

    [Fact]
    public async Task CronTools_ListJobs_ShowsCronJobDetails()
    {
        var mockCronService = new Mock<ICronService>();
        var job = new CronJob
        {
            Id = "test-job-123",
            Name = "Daily Reminder",
            Message = "Remember to check emails",
            Schedule = new CronSchedule
            {
                Kind = CronScheduleKind.Cron,
                Expression = "0 9 * * *",
                TimeZone = "America/New_York"
            },
            Enabled = true,
            State = new CronJobState
            {
                LastRunAtMs = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds(),
                NextRunAtMs = DateTimeOffset.UtcNow.AddHours(8).ToUnixTimeMilliseconds(),
                LastStatus = "completed"
            }
        };
        mockCronService.Setup(x => x.ListJobs()).Returns(new List<CronJob> { job });

        var tool = CronTools.CreateCronTool(mockCronService.Object, "whatsapp", "123456");
        var func = (AIFunction)tool;

        var result = await func.InvokeAsync(
            new AIFunctionArguments
            {
                ["action"] = "list",
                ["message"] = null,
                ["everySeconds"] = null,
                ["cronExpr"] = null,
                ["tz"] = null,
                ["at"] = null,
                ["jobId"] = null
            },
            CancellationToken.None);

        var output = result?.ToString() ?? "";
        Assert.Contains("Daily Reminder", output);
        Assert.Contains("test-job-123", output);
        Assert.Contains("cron: 0 9 * * * (America/New_York)", output);
        Assert.Contains("enabled=True", output);
        Assert.Contains("last_run=", output);
        Assert.Contains("last_status=completed", output);
        Assert.Contains("next_run=", output);
    }

    [Fact]
    public async Task CronTools_ListJobs_ShowsEveryJobDetails()
    {
        var mockCronService = new Mock<ICronService>();
        var job = new CronJob
        {
            Id = "interval-job",
            Name = "Check Status",
            Message = "Check system status",
            Schedule = new CronSchedule
            {
                Kind = CronScheduleKind.Every,
                EveryMs = 60000 // 60 seconds
            },
            Enabled = true,
            State = new CronJobState()
        };
        mockCronService.Setup(x => x.ListJobs()).Returns(new List<CronJob> { job });

        var tool = CronTools.CreateCronTool(mockCronService.Object, "whatsapp", "123456");
        var func = (AIFunction)tool;

        var result = await func.InvokeAsync(
            new AIFunctionArguments
            {
                ["action"] = "list",
                ["message"] = null,
                ["everySeconds"] = null,
                ["cronExpr"] = null,
                ["tz"] = null,
                ["at"] = null,
                ["jobId"] = null
            },
            CancellationToken.None);

        var output = result?.ToString() ?? "";
        Assert.Contains("every: 60s", output);
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
