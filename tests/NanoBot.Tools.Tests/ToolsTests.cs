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
    public void CreateBrowserTool_ReturnsAITool()
    {
        var tool = BrowserTools.CreateBrowserTool(null);

        Assert.NotNull(tool);
        Assert.IsAssignableFrom<AIFunction>(tool);
    }

    [Fact]
    public async Task BrowserTools_Status_DelegatesToService()
    {
        var mockService = new Mock<IBrowserService>();
        mockService
            .Setup(x => x.GetStatusAsync("openclaw", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BrowserToolResponse
            {
                Ok = true,
                Action = "status",
                Profile = "openclaw",
                Message = "Browser is running"
            });

        var tool = BrowserTools.CreateBrowserTool(mockService.Object);
        var func = (AIFunction)tool;

        var result = await func.InvokeAsync(
            new AIFunctionArguments
            {
                ["action"] = "status",
                ["profile"] = "openclaw",
                ["targetId"] = null,
                ["targetUrl"] = null,
                ["snapshotFormat"] = null,
                ["kind"] = null,
                ["reference"] = null,
                ["text"] = null,
                ["textGone"] = null,
                ["key"] = null,
                ["timeoutMs"] = null,
                ["scrollBy"] = null,
                ["selector"] = null,
                ["maxChars"] = null,
                ["loadState"] = null
            },
            CancellationToken.None);

        var resultText = result?.ToString() ?? string.Empty;
        Assert.Contains("status", resultText);
        Assert.Contains("openclaw", resultText);
        mockService.Verify(x => x.GetStatusAsync("openclaw", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BrowserTools_Content_DelegatesToService()
    {
        var mockService = new Mock<IBrowserService>();
        mockService
            .Setup(x => x.GetContentAsync("t1", "main", 2000, "openclaw", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BrowserToolResponse
            {
                Ok = true,
                Action = "content",
                Profile = "openclaw",
                TargetId = "t1",
                Content = "headline"
            });

        var tool = BrowserTools.CreateBrowserTool(mockService.Object);
        var func = (AIFunction)tool;

        var result = await func.InvokeAsync(
            new AIFunctionArguments
            {
                ["action"] = "content",
                ["profile"] = "openclaw",
                ["targetId"] = "t1",
                ["targetUrl"] = null,
                ["snapshotFormat"] = null,
                ["kind"] = null,
                ["reference"] = null,
                ["text"] = null,
                ["textGone"] = null,
                ["key"] = null,
                ["timeoutMs"] = null,
                ["scrollBy"] = null,
                ["selector"] = "main",
                ["maxChars"] = 2000,
                ["loadState"] = null
            },
            CancellationToken.None);

        var resultText = result?.ToString() ?? string.Empty;
        Assert.Contains("content", resultText);
        Assert.Contains("headline", resultText);
        mockService.Verify(x => x.GetContentAsync("t1", "main", 2000, "openclaw", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BrowserTools_ActWait_DelegatesLoadStateAndText()
    {
        var mockService = new Mock<IBrowserService>();
        mockService
            .Setup(x => x.ExecuteActionAsync(
                It.Is<BrowserActionRequest>(r =>
                    r.Kind == "wait" &&
                    r.LoadState == "domcontentloaded" &&
                    r.Text == "latest news" &&
                    r.TextGone == "loading..." &&
                    r.TimeoutMs == 1500),
                "t1",
                "openclaw",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BrowserToolResponse
            {
                Ok = true,
                Action = "act",
                Profile = "openclaw",
                TargetId = "t1",
                Message = "Action executed: wait(condition)"
            });

        var tool = BrowserTools.CreateBrowserTool(mockService.Object);
        var func = (AIFunction)tool;

        var result = await func.InvokeAsync(
            new AIFunctionArguments
            {
                ["action"] = "act",
                ["profile"] = "openclaw",
                ["targetId"] = "t1",
                ["targetUrl"] = null,
                ["snapshotFormat"] = null,
                ["kind"] = "wait",
                ["reference"] = null,
                ["text"] = "latest news",
                ["textGone"] = "loading...",
                ["key"] = null,
                ["timeoutMs"] = 1500,
                ["scrollBy"] = null,
                ["selector"] = null,
                ["maxChars"] = null,
                ["loadState"] = "domcontentloaded"
            },
            CancellationToken.None);

        var resultText = result?.ToString() ?? string.Empty;
        Assert.Contains("wait(condition)", resultText);
        mockService.VerifyAll();
    }

    [Fact]
    public async Task BrowserTools_ActHover_DelegatesTimeout()
    {
        var mockService = new Mock<IBrowserService>();
        mockService
            .Setup(x => x.ExecuteActionAsync(
                It.Is<BrowserActionRequest>(r =>
                    r.Kind == "hover" &&
                    r.Ref == "1" &&
                    r.TimeoutMs == 1200),
                "t1",
                "openclaw",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BrowserToolResponse
            {
                Ok = true,
                Action = "act",
                Profile = "openclaw",
                TargetId = "t1",
                Message = "Action executed: hover"
            });

        var tool = BrowserTools.CreateBrowserTool(mockService.Object);
        var func = (AIFunction)tool;

        var result = await func.InvokeAsync(
            new AIFunctionArguments
            {
                ["action"] = "act",
                ["profile"] = "openclaw",
                ["targetId"] = "t1",
                ["targetUrl"] = null,
                ["snapshotFormat"] = null,
                ["kind"] = "hover",
                ["reference"] = "1",
                ["text"] = null,
                ["textGone"] = null,
                ["key"] = null,
                ["timeoutMs"] = 1200,
                ["scrollBy"] = null,
                ["selector"] = null,
                ["maxChars"] = null,
                ["loadState"] = null
            },
            CancellationToken.None);

        var resultText = result?.ToString() ?? string.Empty;
        Assert.Contains("hover", resultText);
        mockService.VerifyAll();
    }

    [Fact]
    public async Task ToolProvider_DefaultTools_ContainsBrowserTool()
    {
        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();

        var tools = await ToolProvider.CreateDefaultToolsAsync(serviceProvider);

        Assert.Contains(tools, t => t.Name == "browser");
    }

    [SkipIfPlaywrightNotInstalled]
    public async Task BrowserService_StartOpenContentStop_UsesRealPlaywright()
    {
        var workspaceMock = new Mock<IWorkspaceManager>();
        using var browserService = new BrowserService(workspaceMock.Object);

        var start = await browserService.StartAsync("openclaw");
        Assert.True(start.Ok);

        var html = "<html><body><main><article>latest news: test headline</article></main><div id='loading'>loading...</div><button>Read more</button><script>setTimeout(() => { const el = document.getElementById('loading'); if (el) el.remove(); }, 150);</script></body></html>";
        var targetUrl = "data:text/html," + Uri.EscapeDataString(html);

        var opened = await browserService.OpenTabAsync(targetUrl, "openclaw");
        Assert.True(opened.Ok);
        Assert.False(string.IsNullOrWhiteSpace(opened.TargetId));

        var targetId = opened.TargetId!;

        var waitResult = await browserService.ExecuteActionAsync(
            new BrowserActionRequest { Kind = "wait", TimeoutMs = 1500, LoadState = "load", Text = "latest news" },
            targetId,
            "openclaw");
        Assert.True(waitResult.Ok);

        var waitTextGoneResult = await browserService.ExecuteActionAsync(
            new BrowserActionRequest { Kind = "wait", TimeoutMs = 1500, TextGone = "loading..." },
            targetId,
            "openclaw");
        Assert.True(waitTextGoneResult.Ok);

        var snapshotResult = await browserService.GetSnapshotAsync(targetId, "ai", "openclaw");
        Assert.True(snapshotResult.Ok);

        var hoverRef = snapshotResult.Refs?.Keys.FirstOrDefault();
        Assert.False(string.IsNullOrWhiteSpace(hoverRef));

        var hoverResult = await browserService.ExecuteActionAsync(
            new BrowserActionRequest { Kind = "hover", Ref = hoverRef, TimeoutMs = 1200 },
            targetId,
            "openclaw");
        Assert.True(hoverResult.Ok);

        var pressResult = await browserService.ExecuteActionAsync(
            new BrowserActionRequest { Kind = "press", Key = "Tab" },
            targetId,
            "openclaw");
        Assert.True(pressResult.Ok);

        var contentResult = await browserService.GetContentAsync(targetId, "main", 2000, "openclaw");
        Assert.True(contentResult.Ok);
        Assert.Contains("latest news", contentResult.Content ?? string.Empty);

        var tabs = await browserService.GetTabsAsync("openclaw");
        Assert.Contains(tabs, t => t.TargetId == targetId);

        var stopped = await browserService.StopAsync("openclaw");
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

            var start = await browserService.StartAsync("openclaw");
            Assert.True(start.Ok);

            var open = await browserService.OpenTabAsync("https://www.baidu.com", "openclaw");
            Assert.True(open.Ok);
            Assert.False(string.IsNullOrWhiteSpace(open.TargetId));

            var response = await browserService.CaptureSnapshotAsync(
                open.TargetId!,
                "ai",
                "openclaw",
                "webui:baidu-snapshot-test");

            Assert.True(response.Ok);
            Assert.False(string.IsNullOrWhiteSpace(response.Snapshot));
            Assert.False(string.IsNullOrWhiteSpace(response.ImagePath));

            var localPath = Path.Combine(sessionsPath, response.ImagePath!.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(localPath), $"Snapshot file not found: {localPath}");

            Console.WriteLine($"Snapshot image relative path: {response.ImagePath}");
            Console.WriteLine($"Snapshot image local path: {localPath}");
            Console.WriteLine($"Snapshot image url: /api/files/sessions/{response.ImagePath.Replace('\\', '/')}");

            var stop = await browserService.StopAsync("openclaw");
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

            var start = await browserService.StartAsync("openclaw");
            Assert.True(start.Ok);

            var html = "<html><body><main><h1>fallback snapshot</h1></main></body></html>";
            var targetUrl = "data:text/html," + Uri.EscapeDataString(html);
            var open = await browserService.OpenTabAsync(targetUrl, "openclaw");
            Assert.True(open.Ok);

            var response = await browserService.CaptureSnapshotAsync(open.TargetId!, "ai", "openclaw", null);
            Assert.True(response.Ok);
            Assert.False(string.IsNullOrWhiteSpace(response.ImagePath));
            Assert.StartsWith("fallback_openclaw/", response.ImagePath, StringComparison.Ordinal);

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
