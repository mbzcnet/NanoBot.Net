// Copyright (c) NanoBot. All rights reserved.

using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using NanoBot.Agent;
using NanoBot.Core.Configuration;
using NanoBot.Core.Skills;
using NanoBot.Core.Workspace;
using NanoBot.Providers;
using Xunit;
using Xunit.Abstractions;

namespace NanoBot.Agent.Tests.Integration;

/// <summary>
/// Diagnostic tests using NanoBot's own Agent classes.
/// This tests the full NanoBot pipeline: NanoBotAgentFactory → AgentRuntime.
/// </summary>
public class NanoBotAgentDiagnosticTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _ollamaApiBase = "http://172.16.3.220:11435/v1";
    private readonly string _ollamaModel = "qwen3.5";
    private readonly IChatClient _chatClient;
    private readonly string _testWorkspace;

    public NanoBotAgentDiagnosticTests(ITestOutputHelper output)
    {
        _output = output;
        _loggerFactory = LoggerFactory.Create(builder => builder
            .AddConsole()
            .SetMinimumLevel(LogLevel.Information));

        var logger = _loggerFactory.CreateLogger<ChatClientFactory>();
        var factory = new ChatClientFactory(logger);
        _chatClient = factory.CreateChatClient("openai", _ollamaModel, "ollama", _ollamaApiBase);

        _testWorkspace = Path.Combine(Path.GetTempPath(), $"nanobot_diagnostic_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testWorkspace);
        Directory.CreateDirectory(Path.Combine(_testWorkspace, "memory"));
        Directory.CreateDirectory(Path.Combine(_testWorkspace, "sessions"));
        Directory.CreateDirectory(Path.Combine(_testWorkspace, "skills"));

        _output.WriteLine($"Test workspace: {_testWorkspace}");
    }

    public void Dispose()
    {
        _chatClient?.Dispose();
        _loggerFactory?.Dispose();
        try
        {
            if (Directory.Exists(_testWorkspace))
                Directory.Delete(_testWorkspace, recursive: true);
        }
        catch { }
    }

    private static bool EnsureEnabled()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("NANOBOT_OLLAMA_INTEGRATION"),
            "1",
            StringComparison.OrdinalIgnoreCase);
    }

    [Description("Get the weather for a given location.")]
    static string GetWeather([Description("The location to get the weather for.")] string location)
        => $"The weather in {location} is cloudy with a high of 15°C.";

    [Fact]
    public async Task Test_NanoBotAgentFactory_SimpleTool_ShouldCallTool()
    {
        _output.WriteLine("=== Test: NanoBotAgentFactory with simple tool ===");

        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(GetWeather, new AIFunctionFactoryOptions
            {
                Name = "get_weather",
                Description = "Get the weather for a given location."
            })
        };

        var workspaceMock = CreateWorkspaceMock();
        var skillsLoaderMock = CreateSkillsLoaderMock();

        var agent = NanoBotAgentFactory.Create(
            _chatClient,
            workspaceMock.Object,
            skillsLoaderMock.Object,
            tools: tools,
            loggerFactory: _loggerFactory);

        _output.WriteLine($"Agent created: {agent.Name}");
        _output.WriteLine($"Agent Instructions length: {agent.Instructions?.Length ?? 0}");

        var session = await agent.CreateSessionAsync();
        var response = await agent.RunAsync("What's the weather in Beijing?", session);

        var responseText = response.Text ?? "";
        var hasToolCall = response.Messages.Any(m => m.Contents.Any(c => c is FunctionCallContent));

        _output.WriteLine($"Response: {Truncate(responseText, 200)}");
        _output.WriteLine($"Has tool call: {hasToolCall}");

        if (hasToolCall)
        {
            foreach (var msg in response.Messages)
            {
                var calls = msg.Contents.OfType<FunctionCallContent>();
                foreach (var call in calls)
                {
                    _output.WriteLine($"  Tool: {call.Name}, Args: {call.Arguments?.Count ?? 0}");
                }
            }
        }

        Assert.True(hasToolCall, "Tool should be called for weather query");
    }

    [Fact]
    public async Task Test_NanoBotAgentFactory_BrowserTool_ShouldCallTool()
    {
        _output.WriteLine("=== Test: NanoBotAgentFactory with browser tool ===");

        var browserTool = NanoBot.Tools.BuiltIn.BrowserTools.CreateBrowserOpenTool(browserService: null);

        var tools = new List<AITool> { browserTool };

        var workspaceMock = CreateWorkspaceMock();
        var skillsLoaderMock = CreateSkillsLoaderMock();

        var agent = NanoBotAgentFactory.Create(
            _chatClient,
            workspaceMock.Object,
            skillsLoaderMock.Object,
            tools: tools,
            loggerFactory: _loggerFactory);

        _output.WriteLine($"Agent created: {agent.Name}");

        var session = await agent.CreateSessionAsync();
        var response = await agent.RunAsync("Open https://www.baidu.com in browser", session);

        var responseText = response.Text ?? "";
        var hasToolCall = response.Messages.Any(m => m.Contents.Any(c => c is FunctionCallContent));

        _output.WriteLine($"Response: {Truncate(responseText, 200)}");
        _output.WriteLine($"Has tool call: {hasToolCall}");

        if (hasToolCall)
        {
            foreach (var msg in response.Messages)
            {
                var calls = msg.Contents.OfType<FunctionCallContent>();
                foreach (var call in calls)
                {
                    _output.WriteLine($"  Tool: {call.Name}, Args: {call.Arguments?.Count ?? 0}");
                    if (call.Arguments != null)
                    {
                        foreach (var arg in call.Arguments)
                        {
                            _output.WriteLine($"    Arg: {arg.Key} = {arg.Value}");
                        }
                    }
                }
            }
        }

        Assert.True(hasToolCall, "Tool should be called for browser query");
    }

    [Fact]
    public async Task Test_NanoBotAgentFactory_FileTools_ShouldCallTool()
    {
        if (!EnsureEnabled()) return;

        _output.WriteLine("=== Test: NanoBotAgentFactory with file tools ===");

        var tools = new List<AITool>
        {
            NanoBot.Tools.BuiltIn.FileTools.CreateReadFileTool(),
            NanoBot.Tools.BuiltIn.FileTools.CreateListDirTool()
        };

        var workspaceMock = CreateWorkspaceMock();
        var skillsLoaderMock = CreateSkillsLoaderMock();

        var agent = NanoBotAgentFactory.Create(
            _chatClient,
            workspaceMock.Object,
            skillsLoaderMock.Object,
            tools: tools,
            loggerFactory: _loggerFactory);

        _output.WriteLine($"Agent created: {agent.Name}");

        var session = await agent.CreateSessionAsync();
        var response = await agent.RunAsync("List files in /tmp directory", session);

        var responseText = response.Text ?? "";
        var hasToolCall = response.Messages.Any(m => m.Contents.Any(c => c is FunctionCallContent));

        _output.WriteLine($"Response: {Truncate(responseText, 200)}");
        _output.WriteLine($"Has tool call: {hasToolCall}");

        if (hasToolCall)
        {
            foreach (var msg in response.Messages)
            {
                var calls = msg.Contents.OfType<FunctionCallContent>();
                foreach (var call in calls)
                {
                    _output.WriteLine($"  Tool: {call.Name}, Args: {call.Arguments?.Count ?? 0}");
                }
            }
        }

        Assert.True(hasToolCall, "Tool should be called for file listing query");
    }

    private Mock<IWorkspaceManager> CreateWorkspaceMock()
    {
        var mock = new Mock<IWorkspaceManager>();
        mock.Setup(w => w.GetWorkspacePath()).Returns(_testWorkspace);
        mock.Setup(w => w.GetAgentsFile()).Returns(Path.Combine(_testWorkspace, "AGENTS.md"));
        mock.Setup(w => w.GetSoulFile()).Returns(Path.Combine(_testWorkspace, "SOUL.md"));
        mock.Setup(w => w.GetUserFile()).Returns(Path.Combine(_testWorkspace, "USER.md"));
        mock.Setup(w => w.GetToolsFile()).Returns(Path.Combine(_testWorkspace, "TOOLS.md"));
        mock.Setup(w => w.GetMemoryFile()).Returns(Path.Combine(_testWorkspace, "memory", "MEMORY.md"));
        mock.Setup(w => w.GetMemoryPath()).Returns(Path.Combine(_testWorkspace, "memory"));
        mock.Setup(w => w.GetSessionsPath()).Returns(Path.Combine(_testWorkspace, "sessions"));
        mock.Setup(w => w.GetSkillsPath()).Returns(Path.Combine(_testWorkspace, "skills"));
        mock.Setup(w => w.GetHeartbeatFile()).Returns(Path.Combine(_testWorkspace, "heartbeat.md"));
        return mock;
    }

    private Mock<ISkillsLoader> CreateSkillsLoaderMock()
    {
        var mock = new Mock<ISkillsLoader>();
        mock.Setup(s => s.GetAlwaysSkills()).Returns(new List<string>());
        mock.Setup(s => s.BuildSkillsSummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);
        return mock;
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text ?? "";
        return text[..maxLength] + "...";
    }
}