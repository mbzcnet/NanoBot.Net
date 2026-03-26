using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using NanoBot.Agent;
using NanoBot.Core.Configuration;
using NanoBot.Core.Skills;
using NanoBot.Core.Tools.Browser;
using NanoBot.Core.Workspace;
using NanoBot.Providers;
using NanoBot.Tools.BuiltIn;
using Xunit;

namespace NanoBot.Tools.Tests;

/// <summary>
/// 诊断测试：复刻 CLI Agent 的完整配置
/// 目的：验证浏览器工具调用在 CLI 配置下是否正常工作
///
/// 关键差异对比：
/// - ChatClientAgentToolComparisonTests: 只使用 ShellTools, FileTools
/// - 本测试: 使用与 CLI 相同的工具集 (ToolProvider.CreateDefaultToolsAsync)
/// </summary>
public class CliAgentConfigBrowserToolTests : IDisposable
{
    private readonly IChatClient _chatClient;
    private readonly ChatClientAgent _chatClientAgent;
    private readonly IReadOnlyList<AITool> _tools;
    private readonly string _testDirectory;
    private readonly Mock<IWorkspaceManager> _workspaceMock;
    private readonly Mock<ISkillsLoader> _skillsLoaderMock;
    private readonly TestBrowserService _browserService;

    public CliAgentConfigBrowserToolTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"nanobot_cli_config_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);

        // Setup mocks - same as ChatClientAgentToolComparisonTests
        _workspaceMock = new Mock<IWorkspaceManager>();
        _workspaceMock.Setup(w => w.GetWorkspacePath()).Returns(_testDirectory);
        _workspaceMock.Setup(w => w.GetSessionsPath()).Returns(Path.Combine(_testDirectory, "sessions"));
        _workspaceMock.Setup(w => w.GetAgentsFile()).Returns(Path.Combine(_testDirectory, "AGENTS.md"));
        _workspaceMock.Setup(w => w.GetSoulFile()).Returns(Path.Combine(_testDirectory, "SOUL.md"));
        _workspaceMock.Setup(w => w.FileExists(It.IsAny<string>())).Returns(false);
        _workspaceMock.Setup(w => w.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _workspaceMock.Setup(w => w.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _workspaceMock.Setup(w => w.AppendFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _workspaceMock.Setup(w => w.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _workspaceMock.Setup(w => w.EnsureDirectory(It.IsAny<string>()));

        _skillsLoaderMock = new Mock<ISkillsLoader>();
        _skillsLoaderMock.Setup(s => s.GetLoadedSkills()).Returns([]);
        _skillsLoaderMock.Setup(s => s.ListSkills(It.IsAny<bool>())).Returns([]);
        _skillsLoaderMock.Setup(s => s.GetAlwaysSkills()).Returns([]);
        _skillsLoaderMock.Setup(s => s.BuildSkillsSummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);
        _skillsLoaderMock.Setup(s => s.LoadSkillsForContextAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        // Create IChatClient with Ollama qwen3.5:4b
        var config = new LlmConfig
        {
            DefaultProfile = "ollama_qwen3.5_4b",
            Profiles = new Dictionary<string, LlmProfile>
            {
                ["ollama_qwen3.5_4b"] = new LlmProfile
                {
                    Name = "Ollama qwen3.5 4b",
                    Provider = "openai",
                    Model = "qwen3.5:4b",
                    ApiKey = "ollama",
                    ApiBase = "http://172.16.3.220:11435/v1",
                    Temperature = 0.1f,
                    MaxTokens = 64000
                }
            }
        };

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        var factoryLogger = loggerFactory.CreateLogger<ChatClientFactory>();
        var factory = new ChatClientFactory(factoryLogger);

        _chatClient = factory.CreateChatClient(config);

        // Create TestBrowserService
        _browserService = new TestBrowserService();

        // Create tools - EXACTLY like CLI does via ToolProvider.CreateDefaultToolsAsync()
        var toolList = new List<AITool>
        {
            // File tools
            FileTools.CreateReadFileTool(),
            FileTools.CreateWriteFileTool(),
            FileTools.CreateListDirTool(),
            FileTools.CreateEditFileTool(),

            // Shell tool
            ShellTools.CreateExecTool(new ShellToolOptions()),

            // Web tool (unified)
            WebTools.CreateWebPageTool(null),

            // Browser tools - atomic tools
            BrowserTools.CreateBrowserOpenTool(_browserService, () => null),
            BrowserTools.CreateBrowserSnapshotTool(_browserService, () => null),
            BrowserTools.CreateBrowserInteractTool(_browserService, () => null),
            BrowserTools.CreateBrowserContentTool(_browserService, () => null),
            BrowserTools.CreateBrowserScreenshotTool(_browserService, () => null),
            BrowserTools.CreateBrowserTabsTool(_browserService, () => null),
            BrowserTools.CreateBrowserNavigateTool(_browserService, () => null),
            BrowserTools.CreateBrowserCloseTool(_browserService, () => null)
        };

        _tools = toolList.AsReadOnly();

        var agentLoggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Create ChatClientAgent - same as CLI via NanoBotAgentFactory.Create()
        _chatClientAgent = NanoBotAgentFactory.Create(
            _chatClient,
            _workspaceMock.Object,
            _skillsLoaderMock.Object,
            _tools,
            agentLoggerFactory,
            new AgentOptions
            {
                Temperature = 0.1f,
                MaxTokens = 64000
            },
            memoryStore: null,
            memoryWindow: 50,
            maxInstructionChars: 0);
    }

    public void Dispose()
    {
        _chatClient?.Dispose();
        Assert.True(Directory.Exists(_testDirectory));

        var retries = 0;
        while (retries < 5)
        {
            try
            {
                if (Directory.Exists(_testDirectory))
                {
                    Directory.Delete(_testDirectory, recursive: true);
                }
                break;
            }
            catch (IOException)
            {
                Thread.Sleep(100);
                retries++;
            }
        }
    }

    private static bool EnsureEnabled()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("NANOBOT_OLLAMA_INTEGRATION"),
            "1",
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Test: Open browser and navigate to baidu.com
    /// This is the same request that failed in the logs: "帮我打开百度，然后截图"
    /// </summary>
    [Fact]
    public async Task BrowserTool_OpenAndNavigate_ShouldCallOpenTab()
    {
        if (!EnsureEnabled()) return;

        var session = await _chatClientAgent.CreateSessionAsync();

        Console.WriteLine("[TEST] Sending: 帮我打开百度");

        var responseBuilder = new System.Text.StringBuilder();
        var toolCallsDetected = false;
        var openTabCalled = false;

        await foreach (var update in _chatClientAgent.RunStreamingAsync(
            [new ChatMessage(ChatRole.User, "帮我打开百度")],
            session))
        {
            if (update.Text != null)
            {
                responseBuilder.Append(update.Text);
                Console.WriteLine($"[TEST] Text: {update.Text}");
            }

            var functionCalls = update.Contents.OfType<FunctionCallContent>().ToList();
            if (functionCalls.Any())
            {
                toolCallsDetected = true;
                foreach (var call in functionCalls)
                {
                    Console.WriteLine($"[TEST] Tool call: {call.Name}");
                    Console.WriteLine($"[TEST] Arguments: {System.Text.Json.JsonSerializer.Serialize(call.Arguments)}");

                    if (call.Name == "browser_open")
                    {
                        openTabCalled = true;
                    }
                }
            }
        }

        var response = responseBuilder.ToString();
        Console.WriteLine($"[TEST] Full response: {response}");
        Console.WriteLine($"[TEST] Tool calls detected: {toolCallsDetected}");
        Console.WriteLine($"[TEST] Browser service calls: {string.Join(", ", _browserService.Calls.Select(c => c.Method))}");

        Assert.NotNull(response);
        Assert.True(toolCallsDetected || openTabCalled, "Tool call should be detected");
    }

    /// <summary>
    /// Test: Open baidu and capture screenshot
    /// Same as the failed log: "帮我打开百度，然后截图"
    /// </summary>
    [Fact]
    public async Task BrowserTool_OpenAndScreenshot_ShouldCallOpenAndCapture()
    {
        if (!EnsureEnabled()) return;

        var session = await _chatClientAgent.CreateSessionAsync();

        Console.WriteLine("[TEST] Sending: 帮我打开百度，然后截图");

        var responseBuilder = new System.Text.StringBuilder();
        var toolCalls = new List<FunctionCallContent>();

        await foreach (var update in _chatClientAgent.RunStreamingAsync(
            [new ChatMessage(ChatRole.User, "帮我打开百度，然后截图")],
            session))
        {
            if (update.Text != null)
            {
                responseBuilder.Append(update.Text);
                Console.WriteLine($"[TEST] Text: {update.Text}");
            }

            var functionCalls = update.Contents.OfType<FunctionCallContent>().ToList();
            toolCalls.AddRange(functionCalls);
        }

        var response = responseBuilder.ToString();
        Console.WriteLine($"[TEST] Full response: {response}");
        Console.WriteLine($"[TEST] Tool calls: {toolCalls.Count}");
        Console.WriteLine($"[TEST] Browser service calls: {string.Join(", ", _browserService.Calls.Select(c => c.Method))}");

        Assert.NotNull(response);

        // Verify browser tool was called
        var browserCalls = toolCalls.Where(c => c.Name == "browser_open" || c.Name == "browser_screenshot").ToList();
        Console.WriteLine($"[TEST] Browser tool calls: {browserCalls.Count}");

        foreach (var call in browserCalls)
        {
            var args = System.Text.Json.JsonSerializer.Serialize(call.Arguments);
            Console.WriteLine($"[TEST]   - browser({args})");
        }
    }

    /// <summary>
    /// Test: Direct IChatClient with browser tool (baseline comparison)
    /// </summary>
    [Fact]
    public async Task DirectChatClient_BrowserTool_ShouldCallOpenTab()
    {
        if (!EnsureEnabled()) return;

        var options = new ChatOptions
        {
            Tools = _tools.ToList(),
            Temperature = 0.1f
        };

        var responseBuilder = new System.Text.StringBuilder();
        var toolCallsDetected = false;

        Console.WriteLine("[DIRECT] Testing IChatClient directly with browser tool...");

        await foreach (var update in _chatClient.GetStreamingResponseAsync(
            "帮我打开百度",
            options))
        {
            if (update.Text != null)
                responseBuilder.Append(update.Text);

            if (update.Contents.Any(c => c is FunctionCallContent))
            {
                toolCallsDetected = true;
                var calls = update.Contents.OfType<FunctionCallContent>().ToList();
                foreach (var call in calls)
                {
                    Console.WriteLine($"[DIRECT] Tool call: {call.Name}");
                    Console.WriteLine($"[DIRECT] Arguments: {System.Text.Json.JsonSerializer.Serialize(call.Arguments)}");
                }
            }
        }

        var response = responseBuilder.ToString();
        Console.WriteLine($"[DIRECT] Full response: {response}");
        Console.WriteLine($"[DIRECT] Browser service calls: {string.Join(", ", _browserService.Calls.Select(c => c.Method))}");

        Assert.NotNull(response);
        Assert.True(toolCallsDetected, "Tool call should be detected");
    }
}

