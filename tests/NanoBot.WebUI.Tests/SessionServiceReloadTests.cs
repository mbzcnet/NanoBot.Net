using FluentAssertions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Moq;
using NanoBot.Agent;
using NanoBot.Core.Storage;
using NanoBot.Core.Workspace;
using NanoBot.WebUI.Services;
using Xunit;

namespace NanoBot.WebUI.Tests;

public class SessionServiceReloadTests : IDisposable
{
    private readonly string _root;
    private readonly string _sessionsPath;

    public SessionServiceReloadTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"nanobot_webui_tests_{Guid.NewGuid():N}");
        _sessionsPath = Path.Combine(_root, "sessions");
        Directory.CreateDirectory(_sessionsPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task GetMessagesAsync_ShouldConsolidateToolCallAndToolResultIntoAssistantMessage()
    {
        var sessionId = "reload1";
        var file = Path.Combine(_sessionsPath, "webui_reload1.jsonl");
        await File.WriteAllLinesAsync(file,
        [
            "{\"role\":\"assistant\",\"content\":\"正在打开页面\",\"tool_calls\":[{\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"browser\",\"arguments\":\"{\\\"action\\\":\\\"open\\\",\\\"targetUrl\\\":\\\"https://www.bing.com\\\"}\"}}]}",
            "{\"role\":\"tool\",\"tool_call_id\":\"call_1\",\"name\":\"browser\",\"content\":\"{\\\"ok\\\":true,\\\"action\\\":\\\"open\\\"}\"}"
        ]);

        var service = CreateService();
        var messages = await service.GetMessagesAsync(sessionId);

        messages.Should().HaveCount(1);
        var assistant = messages.Single();
        assistant.Role.Should().Be("assistant");
        assistant.ToolExecutions.Should().HaveCount(1);
        assistant.ToolExecutions[0].Name.Should().Be("browser");
        assistant.ToolExecutions[0].Arguments.Should().Contain("targetUrl");
        assistant.ToolExecutions[0].Output.Should().Contain("\"ok\":true");
    }

    [Fact]
    public async Task GetMessagesAsync_SnapshotToolResult_ShouldAppendMarkdownImageToOut()
    {
        var sessionId = "reload2";
        var file = Path.Combine(_sessionsPath, "webui_reload2.jsonl");
        var imagePath = Path.Combine(_sessionsPath, "webui:reload2", "screenshots", "shot.png");
        var escapedImagePath = imagePath.Replace("\\", "\\\\");
        await File.WriteAllLinesAsync(file,
        [
            "{\"role\":\"assistant\",\"content\":\"准备截图\",\"tool_calls\":[{\"id\":\"call_2\",\"type\":\"function\",\"function\":{\"name\":\"browser\",\"arguments\":\"{\\\"action\\\":\\\"snapshot\\\",\\\"targetId\\\":\\\"t1\\\"}\"}}]}",
            $"{{\"role\":\"tool\",\"tool_call_id\":\"call_2\",\"name\":\"browser\",\"content\":\"{{\\\"ok\\\":true,\\\"action\\\":\\\"snapshot\\\",\\\"imagePath\\\":\\\"{escapedImagePath}\\\"}}\"}}"
        ]);

        var service = CreateService();
        var messages = await service.GetMessagesAsync(sessionId);

        var output = messages.Single().ToolExecutions.Single().Output;
        output.Should().Contain("![snapshot](");
        output.Should().Contain("/api/files/sessions/");
        output.Should().Contain("shot.png");
    }

    private SessionService CreateService()
    {
        var logger = new Mock<ILogger<SessionService>>();
        var sessionManager = new Mock<ISessionManager>();
        var workspace = new Mock<IWorkspaceManager>();
        var fileStorage = new Mock<IFileStorageService>();

        sessionManager.Setup(s => s.GetOrCreateSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentSession)null!);
        workspace.Setup(w => w.GetSessionsPath()).Returns(_sessionsPath);

        return new SessionService(logger.Object, sessionManager.Object, workspace.Object, fileStorage.Object);
    }
}
