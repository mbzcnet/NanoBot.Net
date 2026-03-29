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
using NanoBot.Tools.BuiltIn;
using Xunit;
using Xunit.Abstractions;

namespace NanoBot.Agent.Tests.Integration;

/// <summary>
/// Integration tests using the actual configuration from ~/.nbot/config.json
/// Tests the full NanoBot pipeline with real LLM configuration.
/// 
/// Configuration is loaded from:
/// 1. Environment variable NBOT_CONFIG_PATH (if set)
/// 2. ~/.nbot/config.json (default)
/// 3. config.json in current directory
/// 
/// Environment variables:
/// - NBOT_CONFIG_PATH: Path to config file
/// - NBOT_LLM_PROVIDER: Override LLM provider (e.g., "ollama", "openai")
/// - NBOT_LLM_MODEL: Override model (e.g., "qwen3.5:4b", "gpt-4o-mini")
/// - NBOT_LLM_API_BASE: Override API base URL
/// - NBOT_LLM_API_KEY: Override API key
/// </summary>
public class ConfiguredAgentIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _testWorkspace;
    private readonly AgentConfig _agentConfig;
    private readonly IChatClient _chatClient;
    private readonly ServiceProvider _serviceProvider;

    public ConfiguredAgentIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _loggerFactory = LoggerFactory.Create(builder => builder
            .AddConsole()
            .SetMinimumLevel(LogLevel.Information));

        _testWorkspace = Path.Combine(Path.GetTempPath(), $"nanobot_configured_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testWorkspace);
        Directory.CreateDirectory(Path.Combine(_testWorkspace, "memory"));
        Directory.CreateDirectory(Path.Combine(_testWorkspace, "sessions"));
        Directory.CreateDirectory(Path.Combine(_testWorkspace, "skills"));

        _output.WriteLine($"Test workspace: {_testWorkspace}");

        // Load configuration
        _agentConfig = LoadAgentConfig();
        _output.WriteLine($"Loaded configuration:");
        _output.WriteLine($"  Provider: {_agentConfig.Llm.Profiles.Values.FirstOrDefault()?.Provider ?? "N/A"}");
        _output.WriteLine($"  Model: {_agentConfig.Llm.Profiles.Values.FirstOrDefault()?.Model ?? "N/A"}");
        _output.WriteLine($"  API Base: {_agentConfig.Llm.Profiles.Values.FirstOrDefault()?.ApiBase ?? "N/A"}");

        // Override with environment variables if set
        OverrideConfigFromEnvironment();

        // Create services
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        // Create chat client
        var factoryLogger = _loggerFactory.CreateLogger<ChatClientFactory>();
        var factory = new ChatClientFactory(factoryLogger);
        var profile = _agentConfig.Llm.Profiles.Values.First();
        _chatClient = factory.CreateChatClient(
            profile.Provider ?? "openai",
            profile.Model ?? "gpt-4o-mini",
            profile.ApiKey ?? "test",
            profile.ApiBase);
    }

    private AgentConfig LoadAgentConfig()
    {
        // Try to load from config file
        var configPath = Environment.GetEnvironmentVariable("NBOT_CONFIG_PATH");
        if (string.IsNullOrEmpty(configPath))
        {
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var defaultPath = Path.Combine(homeDir, ".nbot", "config.json");
            if (File.Exists(defaultPath))
            {
                configPath = defaultPath;
            }
            else if (File.Exists("config.json"))
            {
                configPath = "config.json";
            }
        }

        if (!string.IsNullOrEmpty(configPath) && File.Exists(configPath))
        {
            _output.WriteLine($"Loading config from: {configPath}");
            return ConfigurationLoader.Load(configPath);
        }

        _output.WriteLine("No config file found, using default configuration");
        return CreateDefaultConfig();
    }

    private void OverrideConfigFromEnvironment()
    {
        var profile = _agentConfig.Llm.Profiles.Values.FirstOrDefault();
        if (profile == null)
        {
            _agentConfig.Llm.Profiles["default"] = new LlmProfile { Name = "default" };
            profile = _agentConfig.Llm.Profiles["default"];
            _agentConfig.Llm.DefaultProfile = "default";
        }

        var provider = Environment.GetEnvironmentVariable("NBOT_LLM_PROVIDER");
        if (!string.IsNullOrEmpty(provider))
        {
            profile.Provider = provider;
            _output.WriteLine($"Overriding provider: {provider}");
        }

        var model = Environment.GetEnvironmentVariable("NBOT_LLM_MODEL");
        if (!string.IsNullOrEmpty(model))
        {
            profile.Model = model;
            _output.WriteLine($"Overriding model: {model}");
        }

        var apiBase = Environment.GetEnvironmentVariable("NBOT_LLM_API_BASE");
        if (!string.IsNullOrEmpty(apiBase))
        {
            profile.ApiBase = apiBase;
            _output.WriteLine($"Overriding API base: {apiBase}");
        }

        var apiKey = Environment.GetEnvironmentVariable("NBOT_LLM_API_KEY");
        if (!string.IsNullOrEmpty(apiKey))
        {
            profile.ApiKey = apiKey;
            _output.WriteLine("Overriding API key: [SET]");
        }

        // Set workspace path to test directory
        _agentConfig.Workspace.Path = _testWorkspace;
    }

    private static AgentConfig CreateDefaultConfig()
    {
        return new AgentConfig
        {
            Name = "NanoBot",
            Workspace = new WorkspaceConfig { Path = Path.GetTempPath() },
            Llm = new LlmConfig
            {
                DefaultProfile = "default",
                Profiles = new Dictionary<string, LlmProfile>
                {
                    ["default"] = new LlmProfile
                    {
                        Name = "default",
                        Provider = "ollama",
                        Model = "qwen3.5:4b",
                        ApiKey = "ollama",
                        ApiBase = "http://172.16.3.220:11435/v1"
                    }
                }
            },
            Memory = new MemoryConfig
            {
                MemoryWindow = 50
            }
        };
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(_loggerFactory);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton(_agentConfig);
        services.AddSingleton(_agentConfig.Workspace);
        services.AddSingleton(_agentConfig.Llm);
        services.AddSingleton(_agentConfig.Memory);
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

    private ChatClientAgent CreateAgent(IEnumerable<AITool>? tools = null)
    {
        var workspaceMock = CreateWorkspaceMock();
        var skillsLoaderMock = CreateSkillsLoaderMock();

        return NanoBotAgentFactory.Create(
            _chatClient,
            workspaceMock.Object,
            skillsLoaderMock.Object,
            tools: tools?.ToList(),
            loggerFactory: _loggerFactory);
    }

    private static IReadOnlyList<FunctionCallContent> GetToolCalls(AgentResponse response)
    {
        return response.Messages
            .SelectMany(m => m.Contents.OfType<FunctionCallContent>())
            .ToList();
    }

    private static IReadOnlyList<FunctionCallContent> GetToolCallsFromStreaming(
        IAsyncEnumerable<AgentResponseUpdate> streamingResponse,
        CancellationToken cancellationToken = default)
    {
        var toolCalls = new List<FunctionCallContent>();
        var enumrator = streamingResponse.GetAsyncEnumerator(cancellationToken);
        while (enumrator.MoveNextAsync().AsTask().Result)
        {
            var update = enumrator.Current;
            var calls = update.Contents.OfType<FunctionCallContent>();
            toolCalls.AddRange(calls);
        }
        return toolCalls;
    }

    [Description("Get the weather for a given location.")]
    static string GetWeather([Description("The location to get the weather for.")] string location)
        => $"The weather in {location} is cloudy with a high of 15°C.";

    [Fact]
    public async Task Agent_WithConfiguredLlm_ShouldRespondToSimplePrompt()
    {
        _output.WriteLine("=== Test: Agent with configured LLM responds to simple prompt ===");

        var workspaceMock = CreateWorkspaceMock();
        var skillsLoaderMock = CreateSkillsLoaderMock();

        var agent = NanoBotAgentFactory.Create(
            _chatClient,
            workspaceMock.Object,
            skillsLoaderMock.Object,
            loggerFactory: _loggerFactory);

        _output.WriteLine($"Agent created: {agent.Name}");

        var session = await agent.CreateSessionAsync();
        var response = await agent.RunAsync("Hello, please respond with 'OK' if you can hear me", session);

        Assert.NotNull(response);
        Assert.NotEmpty(response.Text);
        _output.WriteLine($"Response: {response.Text}");
    }

    [Fact]
    public async Task Agent_WithConfiguredLlm_ShouldHandleMultipleTurns()
    {
        _output.WriteLine("=== Test: Agent with configured LLM handles multiple turns ===");

        var workspaceMock = CreateWorkspaceMock();
        var skillsLoaderMock = CreateSkillsLoaderMock();

        var agent = NanoBotAgentFactory.Create(
            _chatClient,
            workspaceMock.Object,
            skillsLoaderMock.Object,
            loggerFactory: _loggerFactory);

        var session = await agent.CreateSessionAsync();

        // First turn
        var response1 = await agent.RunAsync("My name is Alice", session);
        Assert.NotNull(response1);
        _output.WriteLine($"Response 1: {response1.Text}");

        // Second turn - should remember context
        var response2 = await agent.RunAsync("What is my name?", session);
        Assert.NotNull(response2);
        _output.WriteLine($"Response 2: {response2.Text}");

        Assert.Contains("Alice", response2.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Agent_WithWeatherTool_ShouldCallWithCorrectLocation()
    {
        _output.WriteLine("=== Test: Agent calls weather tool with correct location ===");

        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(GetWeather, new AIFunctionFactoryOptions
            {
                Name = "get_weather",
                Description = "Get the weather for a given location."
            })
        };

        var agent = CreateAgent(tools);
        var session = await agent.CreateSessionAsync();

        var response = await agent.RunAsync("What's the weather in Tokyo?", session);

        _output.WriteLine($"Response: {Truncate(response.Text ?? "", 300)}");

        // Strict assertion: verify tool was called
        var toolCalls = GetToolCalls(response);
        Assert.NotEmpty(toolCalls);

        // Verify correct tool name
        Assert.Contains(toolCalls, tc => tc.Name == "get_weather");

        // Verify correct argument
        var weatherCall = toolCalls.First(tc => tc.Name == "get_weather");
        var location = weatherCall.Arguments?["location"]?.ToString();
        Assert.Equal("Tokyo", location);
    }

    [Fact]
    public async Task Agent_WithWeatherTool_ShouldCallForShanghai()
    {
        _output.WriteLine("=== Test: Agent calls weather tool for Shanghai ===");

        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(GetWeather, new AIFunctionFactoryOptions
            {
                Name = "get_weather",
                Description = "Get the weather for a given location."
            })
        };

        var agent = CreateAgent(tools);
        var session = await agent.CreateSessionAsync();

        var response = await agent.RunAsync("Tell me the weather in Shanghai, China", session);

        _output.WriteLine($"Response: {Truncate(response.Text ?? "", 300)}");

        // Strict assertion: verify tool was called
        var toolCalls = GetToolCalls(response);
        Assert.NotEmpty(toolCalls);

        // Verify tool name
        var weatherCall = toolCalls.FirstOrDefault(tc => tc.Name == "get_weather");
        Assert.NotNull(weatherCall);

        // Verify argument contains Shanghai
        var location = weatherCall.Arguments?["location"]?.ToString();
        Assert.NotNull(location);
        Assert.Contains("Shanghai", location, StringComparison.OrdinalIgnoreCase);
    }

    #region FileTools Tests

    [Fact]
    public async Task Agent_WithReadFileTool_ShouldCallReadFileWithPath()
    {
        _output.WriteLine("=== Test: Agent calls read_file tool with correct path ===");

        // Arrange: create a test file
        var testFile = Path.Combine(_testWorkspace, "test_read.txt");
        await File.WriteAllTextAsync(testFile, "Hello, World!");

        var tools = new List<AITool>
        {
            FileTools.CreateReadFileTool(_testWorkspace)
        };

        var agent = CreateAgent(tools);
        var session = await agent.CreateSessionAsync();

        // Act
        var response = await agent.RunAsync($"Read the file at {testFile}", session);

        _output.WriteLine($"Response: {Truncate(response.Text ?? "", 300)}");

        // Strict assertion: verify tool was called
        var toolCalls = GetToolCalls(response);
        Assert.True(toolCalls.Count > 0, "At least one tool should be called");

        // Verify correct tool name
        Assert.Contains(toolCalls, tc => tc.Name == "read_file");

        // Verify path argument
        var readCall = toolCalls.First(tc => tc.Name == "read_file");
        var path = readCall.Arguments?["path"]?.ToString();
        Assert.NotNull(path);
        Assert.Equal(testFile, path);
    }

    [Fact]
    public async Task Agent_WithWriteFileTool_ShouldCallWriteFileWithContent()
    {
        _output.WriteLine("=== Test: Agent calls write_file tool with content ===");

        var tools = new List<AITool>
        {
            FileTools.CreateWriteFileTool(_testWorkspace)
        };

        var agent = CreateAgent(tools);
        var session = await agent.CreateSessionAsync();

        // Act
        var response = await agent.RunAsync($"Write 'Test content' to a file named output.txt in {_testWorkspace}", session);

        _output.WriteLine($"Response: {Truncate(response.Text ?? "", 300)}");

        // Strict assertion: verify tool was called
        var toolCalls = GetToolCalls(response);
        Assert.True(toolCalls.Count > 0, "At least one tool should be called");

        // Verify correct tool name
        Assert.Contains(toolCalls, tc => tc.Name == "write_file");

        // Verify write_file was called
        var writeCall = toolCalls.First(tc => tc.Name == "write_file");
        Assert.NotNull(writeCall.Arguments);

        // Verify arguments contain path and content
        var hasPath = writeCall.Arguments?.ContainsKey("path") == true;
        var hasContent = writeCall.Arguments?.ContainsKey("content") == true;
        Assert.True(hasPath || hasContent, "write_file should have path or content argument");
    }

    [Fact]
    public async Task Agent_WithListDirTool_ShouldCallListDir()
    {
        _output.WriteLine("=== Test: Agent calls list_dir tool ===");

        // Arrange: create some files
        Directory.CreateDirectory(Path.Combine(_testWorkspace, "subdir"));
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "file1.txt"), "content1");
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "file2.txt"), "content2");

        var tools = new List<AITool>
        {
            FileTools.CreateListDirTool(_testWorkspace)
        };

        var agent = CreateAgent(tools);
        var session = await agent.CreateSessionAsync();

        // Act
        var response = await agent.RunAsync($"List the files in {_testWorkspace}", session);

        _output.WriteLine($"Response: {Truncate(response.Text ?? "", 300)}");

        // Strict assertion: verify tool was called
        var toolCalls = GetToolCalls(response);
        Assert.True(toolCalls.Count > 0, "At least one tool should be called");

        // Verify correct tool name
        Assert.Contains(toolCalls, tc => tc.Name == "list_dir");

        // Verify list_dir was called
        var listCall = toolCalls.First(tc => tc.Name == "list_dir");
        Assert.NotNull(listCall);
    }

    #endregion

    #region ShellTools Tests

    [Fact]
    public async Task Agent_WithExecTool_ShouldCallExecWithCommand()
    {
        _output.WriteLine("=== Test: Agent calls exec tool with command ===");

        var tools = new List<AITool>
        {
            ShellTools.CreateExecTool(new ShellToolOptions())
        };

        var agent = CreateAgent(tools);
        var session = await agent.CreateSessionAsync();

        // Act
        var response = await agent.RunAsync("Execute the command 'echo hello'", session);

        _output.WriteLine($"Response: {Truncate(response.Text ?? "", 300)}");

        // Strict assertion: verify tool was called
        var toolCalls = GetToolCalls(response);
        Assert.True(toolCalls.Count > 0, "At least one tool should be called");

        // Verify correct tool name
        Assert.Contains(toolCalls, tc => tc.Name == "exec");

        // Verify exec was called
        var execCall = toolCalls.First(tc => tc.Name == "exec");
        Assert.NotNull(execCall.Arguments);

        // Verify command argument exists
        var hasCommand = execCall.Arguments?.ContainsKey("command") == true;
        Assert.True(hasCommand, "exec should have command argument");
    }

    #endregion

    #region BrowserTools Tests

    [Fact]
    public async Task Agent_WithBrowserOpenTool_ShouldCallBrowserOpenWithUrl()
    {
        _output.WriteLine("=== Test: Agent calls browser_open tool with URL ===");

        var tools = new List<AITool>
        {
            BrowserTools.CreateBrowserOpenTool(null)
        };

        var agent = CreateAgent(tools);
        var session = await agent.CreateSessionAsync();

        // Act
        var response = await agent.RunAsync("Open https://example.com in browser", session);

        _output.WriteLine($"Response: {Truncate(response.Text ?? "", 300)}");

        // Strict assertion: verify tool was called
        var toolCalls = GetToolCalls(response);
        Assert.True(toolCalls.Count > 0, "At least one tool should be called");

        // Verify correct tool name
        Assert.Contains(toolCalls, tc => tc.Name == "browser_open");

        // Verify URL argument
        var browserCall = toolCalls.First(tc => tc.Name == "browser_open");
        var url = browserCall.Arguments?["url"]?.ToString();
        Assert.NotNull(url);
        Assert.Contains("example.com", url, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region WebTools Tests

    [Fact]
    public async Task Agent_WithWebPageTool_ShouldCallWebPageWithUrl()
    {
        _output.WriteLine("=== Test: Agent calls web_page tool with URL ===");

        var tools = new List<AITool>
        {
            WebTools.CreateWebPageTool(null, null)
        };

        var agent = CreateAgent(tools);
        var session = await agent.CreateSessionAsync();

        // Act
        var response = await agent.RunAsync("Fetch the content of https://example.com", session);

        _output.WriteLine($"Response: {Truncate(response.Text ?? "", 300)}");

        // Strict assertion: verify tool was called
        var toolCalls = GetToolCalls(response);
        Assert.True(toolCalls.Count > 0, "At least one tool should be called");

        // Verify correct tool name
        Assert.Contains(toolCalls, tc => tc.Name == "web_page");

        // Verify URL argument
        var webCall = toolCalls.First(tc => tc.Name == "web_page");
        var url = webCall.Arguments?["url"]?.ToString();
        Assert.NotNull(url);
        Assert.Contains("example.com", url, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    [Fact]
    public async Task AgentRuntime_Streaming_WithConfiguredLlm_ShouldWork()
    {
        _output.WriteLine("=== Test: AgentRuntime streaming with configured LLM ===");

        var workspaceMock = CreateWorkspaceMock();
        var skillsLoaderMock = CreateSkillsLoaderMock();

        var agent = NanoBotAgentFactory.Create(
            _chatClient,
            workspaceMock.Object,
            skillsLoaderMock.Object,
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
        var updateCount = 0;

        await foreach (var update in runtime.ProcessDirectStreamingAsync(
            "Say 'hello world' in exactly 3 words",
            sessionKey: "test_streaming_session",
            channel: "test",
            chatId: "test"))
        {
            if (update.Text != null)
            {
                responseBuilder.Append(update.Text);
            }
            updateCount++;
        }

        var response = responseBuilder.ToString();
        _output.WriteLine($"Response ({updateCount} updates): {Truncate(response, 200)}");

        Assert.True(updateCount > 0, "Should have received streaming updates");
        Assert.False(string.IsNullOrWhiteSpace(response), "Response should not be empty");
    }

    [Fact]
    public async Task Agent_PreservesHistoryAcrossRequests()
    {
        _output.WriteLine("=== Test: Agent preserves history across requests ===");

        var workspaceMock = CreateWorkspaceMock();
        var skillsLoaderMock = CreateSkillsLoaderMock();

        var agent = NanoBotAgentFactory.Create(
            _chatClient,
            workspaceMock.Object,
            skillsLoaderMock.Object,
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

        var sessionKey = "test_history_session";

        // First request
        var response1 = await runtime.ProcessDirectAsync(
            "Remember this number: 42",
            sessionKey: sessionKey,
            channel: "test",
            chatId: "test");

        _output.WriteLine($"Response 1: {Truncate(response1, 100)}");

        // Second request - should remember
        var response2 = await runtime.ProcessDirectAsync(
            "What number did I tell you to remember?",
            sessionKey: sessionKey,
            channel: "test",
            chatId: "test");

        _output.WriteLine($"Response 2: {Truncate(response2, 100)}");

        Assert.False(string.IsNullOrWhiteSpace(response1), "First response should not be empty");
        Assert.False(string.IsNullOrWhiteSpace(response2), "Second response should not be empty");
    }

    [Fact]
    public async Task AgentRuntime_HandlesNewSessionCommand()
    {
        _output.WriteLine("=== Test: AgentRuntime handles /new command ===");

        var workspaceMock = CreateWorkspaceMock();
        var skillsLoaderMock = CreateSkillsLoaderMock();

        var agent = NanoBotAgentFactory.Create(
            _chatClient,
            workspaceMock.Object,
            skillsLoaderMock.Object,
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

        var response = await runtime.ProcessDirectAsync("/new", sessionKey: "test_new_session");

        _output.WriteLine($"Response: {response}");

        Assert.Contains("New session", response, StringComparison.OrdinalIgnoreCase);
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text ?? "";
        return text[..maxLength] + "...";
    }
}
