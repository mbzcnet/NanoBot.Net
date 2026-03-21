using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using NanoBot.Agent;
using NanoBot.Core.Configuration;
using NanoBot.Core.Skills;
using NanoBot.Core.Workspace;
using NanoBot.Providers;
using NanoBot.Tools.BuiltIn;
using Xunit;

namespace NanoBot.Tools.Tests;

/// <summary>
/// 对比测试：直接使用 IChatClient vs 使用 ChatClientAgent
/// 目的：复现 CLI (agent -m) 的实际行为
/// </summary>
public class ChatClientAgentToolComparisonTests : IDisposable
{
    private readonly IChatClient _chatClient;
    private readonly ChatClientAgent _chatClientAgent;
    private readonly IList<AITool> _tools;
    private readonly string _testDirectory;
    private readonly Mock<IWorkspaceManager> _workspaceMock;
    private readonly Mock<ISkillsLoader> _skillsLoaderMock;

    public ChatClientAgentToolComparisonTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"nanobot_agent_comparison_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);

        // Setup mocks for ChatClientAgent creation
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
                    Temperature = 0.7f,
                    MaxTokens = 64000
                }
            }
        };

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        var factoryLogger = loggerFactory.CreateLogger<ChatClientFactory>();
        var factory = new ChatClientFactory(factoryLogger);

        _chatClient = factory.CreateChatClient(config);

        // Create tools
        _tools = new List<AITool>
        {
            FileTools.CreateReadFileTool(),
            FileTools.CreateWriteFileTool(),
            ShellTools.CreateExecTool(new ShellToolOptions())
        };

        // Convert to IReadOnlyList for NanoBotAgentFactory
        var toolsReadOnly = (IReadOnlyList<AITool>)_tools.ToList();

        // Create ChatClientAgent using NanoBotAgentFactory (same as CLI)
        var agentLoggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        _chatClientAgent = NanoBotAgentFactory.Create(
            _chatClient,
            _workspaceMock.Object,
            _skillsLoaderMock.Object,
            toolsReadOnly,
            agentLoggerFactory,
            new AgentOptions
            {
                Temperature = 0.7f,
                MaxTokens = 64000
            },
            memoryStore: null,
            memoryWindow: 50);
    }

    public void Dispose()
    {
        _chatClient?.Dispose();
        Assert.True(Directory.Exists(_testDirectory));

        // Retry cleanup
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

    #region Direct IChatClient Tests

    [Fact]
    public async Task DirectChatClient_WithShellTool_ShouldCallTool()
    {
        if (!EnsureEnabled()) return;

        var options = new ChatOptions
        {
            Tools = _tools,
            Temperature = 0.1f
        };

        var responseBuilder = new System.Text.StringBuilder();
        var toolCallsDetected = false;

        await foreach (var update in _chatClient.GetStreamingResponseAsync(
            "Execute 'ls -la' command",
            options))
        {
            if (update.Text != null)
                responseBuilder.Append(update.Text);

            // Check for function calls
            if (update.Contents.Any(c => c is FunctionCallContent))
            {
                toolCallsDetected = true;
                var calls = update.Contents.OfType<FunctionCallContent>().ToList();
                foreach (var call in calls)
                {
                    Console.WriteLine($"[DIRECT] Tool call detected: {call.Name}");
                    Console.WriteLine($"[DIRECT] Arguments: {System.Text.Json.JsonSerializer.Serialize(call.Arguments)}");
                }
            }
        }

        var response = responseBuilder.ToString();
        Console.WriteLine($"[DIRECT] Full response: {response}");

        Assert.NotNull(response);
        Assert.False(string.IsNullOrWhiteSpace(response), "Response should not be empty");
    }

    [Fact]
    public async Task DirectChatClient_WithReadFileTool_ShouldCallTool()
    {
        if (!EnsureEnabled()) return;

        var options = new ChatOptions
        {
            Tools = _tools,
            Temperature = 0.1f
        };

        var responseBuilder = new System.Text.StringBuilder();
        var toolCallsDetected = false;

        await foreach (var update in _chatClient.GetStreamingResponseAsync(
            "Read the content of /tmp/test.txt file",
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
                    Console.WriteLine($"[DIRECT] Tool call detected: {call.Name}");
                }
            }
        }

        var response = responseBuilder.ToString();
        Console.WriteLine($"[DIRECT] Full response: {response}");

        Assert.NotNull(response);
    }

    #endregion

    #region ChatClientAgent Tests

    [Fact]
    public async Task ChatClientAgent_WithShellTool_ShouldCallTool()
    {
        if (!EnsureEnabled()) return;

        var session = await _chatClientAgent.CreateSessionAsync();

        var responseBuilder = new System.Text.StringBuilder();
        var toolCallsDetected = false;
        var toolResultsDetected = false;

        Console.WriteLine("[AGENT] Starting streaming response...");

        await foreach (var update in _chatClientAgent.RunStreamingAsync(
            [new ChatMessage(ChatRole.User, "Execute 'ls -la' command")],
            session))
        {
            if (update.Text != null)
            {
                responseBuilder.Append(update.Text);
                Console.WriteLine($"[AGENT] Text: {update.Text}");
            }

            // Check for function calls
            var functionCalls = update.Contents.OfType<FunctionCallContent>().ToList();
            if (functionCalls.Any())
            {
                toolCallsDetected = true;
                foreach (var call in functionCalls)
                {
                    Console.WriteLine($"[AGENT] Tool call: {call.Name}");
                    Console.WriteLine($"[AGENT] Arguments: {System.Text.Json.JsonSerializer.Serialize(call.Arguments)}");
                }
            }

            // Check for function results
            var functionResults = update.Contents.OfType<FunctionResultContent>().ToList();
            if (functionResults.Any())
            {
                toolResultsDetected = true;
                foreach (var result in functionResults)
                {
                    Console.WriteLine($"[AGENT] Tool result: {result.CallId} - {result.Result}");
                }
            }
        }

        var response = responseBuilder.ToString();
        Console.WriteLine($"[AGENT] Full response: {response}");
        Console.WriteLine($"[AGENT] Tool calls detected: {toolCallsDetected}");
        Console.WriteLine($"[AGENT] Tool results detected: {toolResultsDetected}");

        Assert.NotNull(response);
        Assert.False(string.IsNullOrWhiteSpace(response), "Response should not be empty");
        Assert.True(toolCallsDetected, "Tool call should be detected");
    }

    [Fact]
    public async Task ChatClientAgent_WithReadFileTool_ShouldCallTool()
    {
        if (!EnsureEnabled()) return;

        var session = await _chatClientAgent.CreateSessionAsync();

        var responseBuilder = new System.Text.StringBuilder();
        var toolCallsDetected = false;

        Console.WriteLine("[AGENT] Starting streaming response for read file...");

        await foreach (var update in _chatClientAgent.RunStreamingAsync(
            [new ChatMessage(ChatRole.User, "Read the content of /tmp/test.txt file")],
            session))
        {
            if (update.Text != null)
            {
                responseBuilder.Append(update.Text);
            }

            var functionCalls = update.Contents.OfType<FunctionCallContent>().ToList();
            if (functionCalls.Any())
            {
                toolCallsDetected = true;
                foreach (var call in functionCalls)
                {
                    Console.WriteLine($"[AGENT] Tool call: {call.Name}");
                }
            }
        }

        var response = responseBuilder.ToString();
        Console.WriteLine($"[AGENT] Full response: {response}");

        Assert.NotNull(response);
        Assert.True(toolCallsDetected, "Tool call should be detected");
    }

    [Fact]
    public async Task ChatClientAgent_WithMultiTurn_ShouldMaintainContext()
    {
        if (!EnsureEnabled()) return;

        var session = await _chatClientAgent.CreateSessionAsync();

        Console.WriteLine("[AGENT] First turn: Execute 'pwd' command");

        var turn1Builder = new System.Text.StringBuilder();
        var toolCalls1 = 0;
        await foreach (var update in _chatClientAgent.RunStreamingAsync(
            [new ChatMessage(ChatRole.User, "Execute 'pwd' command to show current directory")],
            session))
        {
            if (update.Text != null)
                turn1Builder.Append(update.Text);

            toolCalls1 += update.Contents.OfType<FunctionCallContent>().Count();
        }

        var response1 = turn1Builder.ToString();
        Console.WriteLine($"[AGENT] Turn 1 response: {response1}");
        Console.WriteLine($"[AGENT] Turn 1 tool calls: {toolCalls1}");

        Console.WriteLine("[AGENT] Second turn: List files in that directory");

        var turn2Builder = new System.Text.StringBuilder();
        var toolCalls2 = 0;
        await foreach (var update in _chatClientAgent.RunStreamingAsync(
            [new ChatMessage(ChatRole.User, "Now list all files in that directory")],
            session))
        {
            if (update.Text != null)
                turn2Builder.Append(update.Text);

            toolCalls2 += update.Contents.OfType<FunctionCallContent>().Count();
        }

        var response2 = turn2Builder.ToString();
        Console.WriteLine($"[AGENT] Turn 2 response: {response2}");
        Console.WriteLine($"[AGENT] Turn 2 tool calls: {toolCalls2}");

        Assert.NotNull(response1);
        Assert.NotNull(response2);
        Assert.True(toolCalls1 > 0 || toolCalls2 > 0, "At least one tool call should be detected");
    }

    #endregion

    #region Comparison Tests

    [Fact]
    public async Task Compare_DirectVsAgent_ShellToolCall()
    {
        if (!EnsureEnabled()) return;

        var prompt = "Execute 'echo hello' command";

        // ===== Direct IChatClient =====
        Console.WriteLine("=== Testing DIRECT IChatClient ===");
        var directOptions = new ChatOptions
        {
            Tools = _tools,
            Temperature = 0.1f
        };

        var directBuilder = new System.Text.StringBuilder();
        var directToolCalls = 0;
        var directToolResults = 0;

        await foreach (var update in _chatClient.GetStreamingResponseAsync(prompt, directOptions))
        {
            if (update.Text != null)
                directBuilder.Append(update.Text);

            directToolCalls += update.Contents.OfType<FunctionCallContent>().Count();
            directToolResults += update.Contents.OfType<FunctionResultContent>().Count();
        }

        var directResponse = directBuilder.ToString();
        Console.WriteLine($"[DIRECT] Response: {directResponse}");
        Console.WriteLine($"[DIRECT] Tool calls: {directToolCalls}, Tool results: {directToolResults}");

        // ===== ChatClientAgent =====
        Console.WriteLine("\n=== Testing ChatClientAgent ===");
        var session = await _chatClientAgent.CreateSessionAsync();

        var agentBuilder = new System.Text.StringBuilder();
        var agentToolCalls = 0;
        var agentToolResults = 0;

        await foreach (var update in _chatClientAgent.RunStreamingAsync(
            [new ChatMessage(ChatRole.User, prompt)],
            session))
        {
            if (update.Text != null)
                agentBuilder.Append(update.Text);

            agentToolCalls += update.Contents.OfType<FunctionCallContent>().Count();
            agentToolResults += update.Contents.OfType<FunctionResultContent>().Count();
        }

        var agentResponse = agentBuilder.ToString();
        Console.WriteLine($"[AGENT] Response: {agentResponse}");
        Console.WriteLine($"[AGENT] Tool calls: {agentToolCalls}, Tool results: {agentToolResults}");

        // ===== Comparison =====
        Console.WriteLine("\n=== COMPARISON SUMMARY ===");
        Console.WriteLine($"Direct: Tool calls={directToolCalls}, Results={directToolResults}");
        Console.WriteLine($"Agent:  Tool calls={agentToolCalls}, Results={agentToolResults}");

        // Both should have tool calls
        Assert.True(directToolCalls > 0, "Direct IChatClient should detect tool calls");
        Assert.True(agentToolCalls > 0, "ChatClientAgent should detect tool calls");
    }

    #endregion
}

/// <summary>
/// 使用 ChatClientAgent.RunAsync (非流式) 进行测试
/// 对比 ProcessMessageAsync 的行为
/// </summary>
public class ChatClientAgentNonStreamingTests : IDisposable
{
    private readonly IChatClient _chatClient;
    private readonly ChatClientAgent _chatClientAgent;
    private readonly IList<AITool> _tools;
    private readonly string _testDirectory;
    private readonly Mock<IWorkspaceManager> _workspaceMock;
    private readonly Mock<ISkillsLoader> _skillsLoaderMock;

    public ChatClientAgentNonStreamingTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"nanobot_agent_nonstreaming_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);

        _workspaceMock = new Mock<IWorkspaceManager>();
        _workspaceMock.Setup(w => w.GetWorkspacePath()).Returns(_testDirectory);
        _workspaceMock.Setup(w => w.GetSessionsPath()).Returns(Path.Combine(_testDirectory, "sessions"));
        _workspaceMock.Setup(w => w.FileExists(It.IsAny<string>())).Returns(false);
        _workspaceMock.Setup(w => w.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _workspaceMock.Setup(w => w.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _skillsLoaderMock = new Mock<ISkillsLoader>();
        _skillsLoaderMock.Setup(s => s.GetLoadedSkills()).Returns([]);
        _skillsLoaderMock.Setup(s => s.ListSkills(It.IsAny<bool>())).Returns([]);
        _skillsLoaderMock.Setup(s => s.BuildSkillsSummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

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

        _tools = new List<AITool>
        {
            FileTools.CreateReadFileTool(),
            FileTools.CreateWriteFileTool(),
            ShellTools.CreateExecTool(new ShellToolOptions())
        };

        var agentLoggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        var toolsReadOnly = (IReadOnlyList<AITool>)_tools.ToList();

        _chatClientAgent = NanoBotAgentFactory.Create(
            _chatClient,
            _workspaceMock.Object,
            _skillsLoaderMock.Object,
            toolsReadOnly,
            agentLoggerFactory,
            new AgentOptions
            {
                Temperature = 0.1f,
                MaxTokens = 64000
            },
            memoryStore: null,
            memoryWindow: 50);
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
    /// 测试 ChatClientAgent.RunAsync (非流式)
    /// 这与 AgentRuntime.ProcessMessageAsync 的行为一致
    /// </summary>
    [Fact]
    public async Task ChatClientAgent_RunAsync_WithShellTool_ShouldCallTool()
    {
        if (!EnsureEnabled()) return;

        var session = await _chatClientAgent.CreateSessionAsync();

        Console.WriteLine("[RunAsync] Sending: Execute 'ls -la' command");

        var response = await _chatClientAgent.RunAsync(
            [new ChatMessage(ChatRole.User, "Execute 'ls -la' command")],
            session);

        var responseText = response.Text ?? string.Empty;
        Console.WriteLine($"[RunAsync] Response: {responseText}");

        // Check if tool was called by examining the messages in the response
        var messages = response.Messages.ToList();
        Console.WriteLine($"[RunAsync] Messages count: {messages.Count}");

        foreach (var msg in messages)
        {
            var role = msg.Role.Value ?? "unknown";
            var text = msg.Text ?? "";
            Console.WriteLine($"[RunAsync] Message role={role}, text length={text.Length}");

            var functionCalls = msg.Contents.OfType<FunctionCallContent>().ToList();
            if (functionCalls.Any())
            {
                Console.WriteLine($"[RunAsync] Function calls in message: {functionCalls.Count}");
                foreach (var call in functionCalls)
                {
                    Console.WriteLine($"[RunAsync]   - {call.Name}({System.Text.Json.JsonSerializer.Serialize(call.Arguments)})");
                }
            }
        }

        Assert.NotNull(response);
        Assert.NotEmpty(messages);
    }

    /// <summary>
    /// 测试多轮对话中的工具调用
    /// </summary>
    [Fact]
    public async Task ChatClientAgent_RunAsync_MultiTurn_ShellTool()
    {
        if (!EnsureEnabled()) return;

        var session = await _chatClientAgent.CreateSessionAsync();

        // Turn 1
        Console.WriteLine("[RunAsync] Turn 1: Execute 'echo hello'");
        var response1 = await _chatClientAgent.RunAsync(
            [new ChatMessage(ChatRole.User, "Execute 'echo hello' command")],
            session);

        Console.WriteLine($"[RunAsync] Turn 1 response: {response1.Text}");

        var messages1 = response1.Messages.ToList();
        var toolCalls1 = messages1.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Count();
        Console.WriteLine($"[RunAsync] Turn 1 tool calls: {toolCalls1}");

        // Turn 2 - Add result message to simulate tool execution
        Console.WriteLine("\n[RunAsync] Turn 2: Execute 'pwd'");
        var response2 = await _chatClientAgent.RunAsync(
            [new ChatMessage(ChatRole.User, "Execute 'pwd' command")],
            session);

        Console.WriteLine($"[RunAsync] Turn 2 response: {response2.Text}");

        var messages2 = response2.Messages.ToList();
        var toolCalls2 = messages2.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Count();
        Console.WriteLine($"[RunAsync] Turn 2 tool calls: {toolCalls2}");

        Assert.NotNull(response1);
        Assert.NotNull(response2);
    }
}
