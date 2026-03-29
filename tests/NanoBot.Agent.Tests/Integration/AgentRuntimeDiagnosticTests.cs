// Copyright (c) NanoBot. All rights reserved.

using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NanoBot.Agent;
using NanoBot.Core.Bus;
using NanoBot.Core.Configuration;
using NanoBot.Core.Memory;
using NanoBot.Core.Skills;
using NanoBot.Core.Subagents;
using NanoBot.Core.Workspace;
using NanoBot.Infrastructure.Bus;
using NanoBot.Providers;
using Xunit;
using Xunit.Abstractions;

namespace NanoBot.Agent.Tests.Integration;

/// <summary>
/// Diagnostic tests for AgentRuntime tool calling.
/// This tests the full runtime pipeline: AgentRuntime.ProcessDirectAsync.
/// </summary>
public class AgentRuntimeDiagnosticTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _ollamaApiBase = "http://172.16.3.220:11435/v1";
    private readonly string _ollamaModel = "qwen3.5";
    private readonly string _testWorkspace;
    private readonly IChatClient _chatClient;
    private readonly ServiceProvider _serviceProvider;

    public AgentRuntimeDiagnosticTests(ITestOutputHelper output)
    {
        _output = output;
        _loggerFactory = LoggerFactory.Create(builder => builder
            .AddConsole()
            .SetMinimumLevel(LogLevel.Information));

        _testWorkspace = Path.Combine(Path.GetTempPath(), $"nanobot_runtime_diagnostic_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testWorkspace);
        Directory.CreateDirectory(Path.Combine(_testWorkspace, "memory"));
        Directory.CreateDirectory(Path.Combine(_testWorkspace, "sessions"));
        Directory.CreateDirectory(Path.Combine(_testWorkspace, "skills"));

        _output.WriteLine($"Test workspace: {_testWorkspace}");

        var services = new ServiceCollection();
        services.AddSingleton(_loggerFactory);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));

        var factoryLogger = _loggerFactory.CreateLogger<ChatClientFactory>();
        var factory = new ChatClientFactory(factoryLogger);
        _chatClient = factory.CreateChatClient("openai", _ollamaModel, "ollama", _ollamaApiBase);

        _serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _chatClient?.Dispose();
        _loggerFactory?.Dispose();
        _serviceProvider?.Dispose();
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
    public async Task Test_AgentRuntime_ProcessDirectAsync_SimpleTool_ShouldCallTool()
    {
        _output.WriteLine("=== Test: AgentRuntime.ProcessDirectAsync with simple tool ===");

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

        var sessionManager = new SessionManager(agent, workspaceMock.Object, _loggerFactory.CreateLogger<SessionManager>());
        var messageBus = new MessageBus();

        var runtime = new AgentRuntime(
            agent,
            messageBus,
            sessionManager,
            workspaceMock.Object,
            memoryStore: null,
            subagentManager: null,
            memoryWindow: 50,
            logger: _loggerFactory.CreateLogger<AgentRuntime>());

        var response = await runtime.ProcessDirectAsync(
            "What's the weather in Beijing?",
            sessionKey: "test_session",
            channel: "test",
            chatId: "test");

        _output.WriteLine($"Response: '{Truncate(response, 300)}'");
        _output.WriteLine($"Response length: {response.Length}");
        _output.WriteLine($"Response is null or whitespace: {string.IsNullOrWhiteSpace(response)}");

        var hasToolCall = response.Contains("cloudy") || response.Contains("15°C") || response.Contains("weather");

        _output.WriteLine($"Has tool result in response: {hasToolCall}");

        // Debug: check if response is empty which indicates a problem
        if (string.IsNullOrWhiteSpace(response))
        {
            _output.WriteLine("WARNING: Response is empty! This indicates the non-streaming path is not returning tool results.");
        }

        Assert.False(string.IsNullOrWhiteSpace(response), "Response should not be empty");
        Assert.True(hasToolCall, "Response should contain weather information from tool");
    }

    [Fact]
    public async Task Test_AgentRuntime_ProcessDirectAsync_BrowserTool_ShouldCallTool()
    {
        _output.WriteLine("=== Test: AgentRuntime.ProcessDirectAsync with browser tool ===");

        var tools = new List<AITool>
        {
            NanoBot.Tools.BuiltIn.BrowserTools.CreateBrowserOpenTool(browserService: null)
        };

        var workspaceMock = CreateWorkspaceMock();
        var skillsLoaderMock = CreateSkillsLoaderMock();

        var agent = NanoBotAgentFactory.Create(
            _chatClient,
            workspaceMock.Object,
            skillsLoaderMock.Object,
            tools: tools,
            loggerFactory: _loggerFactory);

        var sessionManager = new SessionManager(agent, workspaceMock.Object, _loggerFactory.CreateLogger<SessionManager>());
        var messageBus = new MessageBus();

        var runtime = new AgentRuntime(
            agent,
            messageBus,
            sessionManager,
            workspaceMock.Object,
            memoryStore: null,
            subagentManager: null,
            memoryWindow: 50,
            logger: _loggerFactory.CreateLogger<AgentRuntime>());

        var response = await runtime.ProcessDirectAsync(
            "Open https://www.baidu.com in browser",
            sessionKey: "test_browser_session",
            channel: "test",
            chatId: "test");

        _output.WriteLine($"Response: {Truncate(response, 300)}");
        _output.WriteLine($"Response length: {response.Length}");

        Assert.False(string.IsNullOrWhiteSpace(response));
    }

    [Fact]
    public async Task Test_AgentRuntime_ProcessDirectStreamingAsync_SimpleTool_ShouldCallTool()
    {
        _output.WriteLine("=== Test: AgentRuntime.ProcessDirectStreamingAsync with simple tool ===");

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

        var sessionManager = new SessionManager(agent, workspaceMock.Object, _loggerFactory.CreateLogger<SessionManager>());
        var messageBus = new MessageBus();

        var runtime = new AgentRuntime(
            agent,
            messageBus,
            sessionManager,
            workspaceMock.Object,
            memoryStore: null,
            subagentManager: null,
            memoryWindow: 50,
            logger: _loggerFactory.CreateLogger<AgentRuntime>());

        var responseBuilder = new System.Text.StringBuilder();
        var hasToolCall = false;
        var toolCallCount = 0;

        await foreach (var update in runtime.ProcessDirectStreamingAsync(
            "What's the weather in Shanghai?",
            sessionKey: "test_streaming_session",
            channel: "test",
            chatId: "test"))
        {
            if (update.Text != null)
            {
                responseBuilder.Append(update.Text);
            }

            var functionCalls = update.Contents.OfType<FunctionCallContent>();
            if (functionCalls.Any())
            {
                hasToolCall = true;
                toolCallCount += functionCalls.Count();
                foreach (var call in functionCalls)
                {
                    _output.WriteLine($"  Tool call: {call.Name}");
                }
            }
        }

        var response = responseBuilder.ToString();
        _output.WriteLine($"Response: {Truncate(response, 300)}");
        _output.WriteLine($"Response length: {response.Length}");
        _output.WriteLine($"Has tool call: {hasToolCall}");
        _output.WriteLine($"Tool call count: {toolCallCount}");

        Assert.False(string.IsNullOrWhiteSpace(response));
        Assert.True(hasToolCall, "Tool should be called in streaming mode");
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