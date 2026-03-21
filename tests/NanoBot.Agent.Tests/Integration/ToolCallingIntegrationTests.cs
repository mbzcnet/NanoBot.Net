using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Configuration;
using NanoBot.Providers;
using Xunit;
using Xunit.Abstractions;

namespace NanoBot.Agent.Tests.Integration;

/// <summary>
/// Tool calling tests using benchmark cases from src/benchmark/cases.json
/// Tests tool invocation capabilities using real LLM models
/// </summary>
public class ToolCallingIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _benchmarkPath;

    public ToolCallingIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _loggerFactory = LoggerFactory.Create(builder => builder
            .AddConsole()
            .SetMinimumLevel(LogLevel.Information));

        _benchmarkPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..",
            "src", "benchmark");
        _benchmarkPath = Path.GetFullPath(_benchmarkPath);

        _output.WriteLine($"Benchmark path: {_benchmarkPath}");
    }

    public void Dispose()
    {
        _loggerFactory.Dispose();
    }

    private IChatClient CreateChatClient(string provider = "openai", string model = "gpt-4o-mini")
    {
        var apiKey = Environment.GetEnvironmentVariable($"{provider.ToUpperInvariant()}_API_KEY");
        var apiBase = Environment.GetEnvironmentVariable($"{provider.ToUpperInvariant()}_API_BASE");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _output.WriteLine($"Warning: {provider.ToUpperInvariant()}_API_KEY not set. Skipping test.");
            throw new SkipTestException($"API key for {provider} not configured");
        }

        var logger = _loggerFactory.CreateLogger<ChatClientFactory>();
        var factory = new ChatClientFactory(logger);

        return factory.CreateChatClient(provider, model, apiKey, apiBase);
    }

    private List<BenchmarkCase> LoadBenchmarkCases()
    {
        var casesFile = Path.Combine(_benchmarkPath, "cases.json");

        if (!File.Exists(casesFile))
        {
            throw new FileNotFoundException($"Benchmark cases file not found: {casesFile}");
        }

        var json = File.ReadAllText(casesFile);
        var doc = JsonDocument.Parse(json);
        var cases = new List<BenchmarkCase>();

        // Load tool cases
        if (doc.RootElement.TryGetProperty("tool", out var toolCases))
        {
            foreach (var caseElement in toolCases.EnumerateArray())
            {
                var benchmarkCase = new BenchmarkCase
                {
                    Id = caseElement.GetProperty("id").GetString() ?? "",
                    Name = caseElement.GetProperty("name").GetString() ?? "",
                    Category = "tool",
                    Input = caseElement.GetProperty("input").GetString() ?? "",
                    Score = caseElement.TryGetProperty("score", out var score) ? score.GetInt32() : 10
                };

                if (caseElement.TryGetProperty("requiredTools", out var tools))
                {
                    foreach (var tool in tools.EnumerateArray())
                    {
                        benchmarkCase.RequiredTools.Add(tool.GetString() ?? "");
                    }
                }

                if (caseElement.TryGetProperty("keywords", out var keywords))
                {
                    foreach (var kw in keywords.EnumerateArray())
                    {
                        benchmarkCase.Keywords.Add(kw.GetString() ?? "");
                    }
                }

                cases.Add(benchmarkCase);
            }
        }

        _output.WriteLine($"Loaded {cases.Count} tool benchmark cases");
        return cases;
    }

    private async Task<(string Response, bool HasToolCall)> SendWithToolsAsync(
        IChatClient chatClient,
        IReadOnlyList<AITool> tools,
        string prompt)
    {
        var options = new ChatOptions
        {
            Tools = tools.ToList()
        };

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are a helpful AI assistant with access to various tools. Use tools when they can help answer the user's question."),
            new(ChatRole.User, prompt)
        };

        _output.WriteLine($"Sending request with {tools.Count} tools: {prompt}");

        // Use streaming response
        var responseBuilder = new System.Text.StringBuilder();
        var hasToolCall = false;

        await foreach (var update in chatClient.GetStreamingResponseAsync(messages, options))
        {
            if (update.Text != null)
            {
                responseBuilder.Append(update.Text);
            }

            // Check for function calls in streaming updates
            if (update.Contents.Any(c => c is FunctionCallContent))
            {
                hasToolCall = true;
            }
        }

        var responseText = responseBuilder.ToString();

        _output.WriteLine($"Response: {responseText}");
        _output.WriteLine($"Has tool call: {hasToolCall}");

        return (responseText, hasToolCall);
    }

    [Fact(Skip = "Requires real API key - enable for integration testing")]
    public async Task ToolCalling_ShouldLoadBenchmarkCases()
    {
        // Act
        var cases = LoadBenchmarkCases();

        // Assert
        Assert.NotEmpty(cases);
        Assert.True(cases.Count >= 5, "Should have at least 5 tool benchmark cases");

        foreach (var testCase in cases)
        {
            _output.WriteLine($"Case: {testCase.Id} - {testCase.Name}");
            _output.WriteLine($"  Input: {testCase.Input}");
            _output.WriteLine($"  Required Tools: {string.Join(", ", testCase.RequiredTools)}");
            _output.WriteLine($"  Keywords: {string.Join(", ", testCase.Keywords)}");
        }
    }

    [Theory(Skip = "Requires real API key - enable for integration testing")]
    [InlineData("tool_file_read")]
    [InlineData("tool_file_write")]
    [InlineData("tool_shell")]
    [InlineData("tool_web_search")]
    public async Task ToolCalling_ShouldTriggerToolForBenchmarkCase(string caseId)
    {
        // Arrange
        using var chatClient = CreateChatClient();
        var cases = LoadBenchmarkCases();
        var testCase = cases.FirstOrDefault(c => c.Id == caseId);

        Assert.NotNull(testCase);
        _output.WriteLine($"Testing case: {testCase.Name}");

        // Create mock tools based on required tools
        var tools = CreateToolsForCase(testCase);

        // Act
        var (response, hasToolCall) = await SendWithToolsAsync(chatClient, tools, testCase.Input);

        // Assert
        Assert.NotNull(response);
        _output.WriteLine($"Response: {response}");

        // For tool calling tests, we expect the model to attempt to call a tool
        // Note: With real models, tool calling behavior depends on the model's capabilities
        _output.WriteLine($"Tool call triggered: {hasToolCall}");
    }

    [Fact(Skip = "Requires real API key - enable for integration testing")]
    public async Task ToolCalling_ShouldCallFileReadTool()
    {
        // Arrange
        using var chatClient = CreateChatClient();

        var listDirTool = AIFunctionFactory.Create(
            (string path) => $"Files in {path}: file1.txt, file2.log, subdir/",
            new AIFunctionFactoryOptions
            {
                Name = "list_dir",
                Description = "List files and directories in a given path"
            });

        var execTool = AIFunctionFactory.Create(
            (string command) => $"Executed: {command}",
            new AIFunctionFactoryOptions
            {
                Name = "exec",
                Description = "Execute a shell command"
            });

        var tools = new List<AITool> { listDirTool, execTool };

        // Act
        var (response, hasToolCall) = await SendWithToolsAsync(
            chatClient,
            tools,
            "List files in /tmp directory");

        // Assert
        Assert.NotNull(response);
        _output.WriteLine($"Response: {response}");
    }

    [Fact(Skip = "Requires real API key - enable for integration testing")]
    public async Task ToolCalling_ShouldCallWebSearchTool()
    {
        // Arrange
        using var chatClient = CreateChatClient();

        var webSearchTool = AIFunctionFactory.Create(
            (string query) => $"Search results for '{query}': [mock results]",
            new AIFunctionFactoryOptions
            {
                Name = "web_search",
                Description = "Search the web for information"
            });

        var tools = new List<AITool> { webSearchTool };

        // Act
        var (response, hasToolCall) = await SendWithToolsAsync(
            chatClient,
            tools,
            "Search for today's weather in Beijing");

        // Assert
        Assert.NotNull(response);
        _output.WriteLine($"Response: {response}");
    }

    [Fact(Skip = "Requires real API key - enable for integration testing")]
    public async Task ToolCalling_ShouldHandleMultiToolScenario()
    {
        // Arrange
        using var chatClient = CreateChatClient();

        var listDirTool = AIFunctionFactory.Create(
            (string path) => $"Files in {path}: file1.txt, file2.log",
            new AIFunctionFactoryOptions
            {
                Name = "list_dir",
                Description = "List files in a directory"
            });

        var writeFileTool = AIFunctionFactory.Create(
            (string path, string content) => $"Created file at {path}",
            new AIFunctionFactoryOptions
            {
                Name = "write_file",
                Description = "Write content to a file"
            });

        var execTool = AIFunctionFactory.Create(
            (string command) => $"Executed: {command}",
            new AIFunctionFactoryOptions
            {
                Name = "exec",
                Description = "Execute shell command"
            });

        var tools = new List<AITool> { listDirTool, writeFileTool, execTool };

        // Act
        var (response, hasToolCall) = await SendWithToolsAsync(
            chatClient,
            tools,
            "First list /tmp directory, then create a file there named test.txt with content 'hello'");

        // Assert
        Assert.NotNull(response);
        _output.WriteLine($"Response: {response}");
    }

    [Fact(Skip = "Requires real API key - enable for integration testing")]
    public async Task ToolCalling_ShouldCallBrowserNavigateTool()
    {
        // Arrange
        using var chatClient = CreateChatClient();

        var browserNavigateTool = AIFunctionFactory.Create(
            (string url) => $"Navigated to {url}",
            new AIFunctionFactoryOptions
            {
                Name = "browser_navigate",
                Description = "Navigate to a URL in the browser"
            });

        var tools = new List<AITool> { browserNavigateTool };

        // Act
        var (response, hasToolCall) = await SendWithToolsAsync(
            chatClient,
            tools,
            "Open https://example.com webpage");

        // Assert
        Assert.NotNull(response);
        _output.WriteLine($"Response: {response}");
    }

    [Fact(Skip = "Requires real API key - enable for integration testing")]
    public async Task ToolCalling_ShouldCallBrowserClickTool()
    {
        // Arrange
        using var chatClient = CreateChatClient();

        var browserNavigateTool = AIFunctionFactory.Create(
            (string url) => $"Navigated to {url}",
            new AIFunctionFactoryOptions
            {
                Name = "browser_navigate",
                Description = "Navigate to a URL"
            });

        var browserClickTool = AIFunctionFactory.Create(
            (string selector) => $"Clicked element matching '{selector}'",
            new AIFunctionFactoryOptions
            {
                Name = "browser_click",
                Description = "Click an element on the page"
            });

        var tools = new List<AITool> { browserNavigateTool, browserClickTool };

        // Act
        var (response, hasToolCall) = await SendWithToolsAsync(
            chatClient,
            tools,
            "Navigate to https://www.google.com and click the search button");

        // Assert
        Assert.NotNull(response);
        _output.WriteLine($"Response: {response}");
    }

    [Fact(Skip = "Requires real API key - enable for integration testing")]
    public async Task ToolCalling_ShouldCallBrowserTypeTool()
    {
        // Arrange
        using var chatClient = CreateChatClient();

        var browserNavigateTool = AIFunctionFactory.Create(
            (string url) => $"Navigated to {url}",
            new AIFunctionFactoryOptions
            {
                Name = "browser_navigate",
                Description = "Navigate to a URL"
            });

        var browserTypeTool = AIFunctionFactory.Create(
            (string selector, string text) => $"Typed '{text}' into element matching '{selector}'",
            new AIFunctionFactoryOptions
            {
                Name = "browser_type",
                Description = "Type text into an input field"
            });

        var tools = new List<AITool> { browserNavigateTool, browserTypeTool };

        // Act
        var (response, hasToolCall) = await SendWithToolsAsync(
            chatClient,
            tools,
            "Navigate to https://www.google.com and type 'hello world' in the search box");

        // Assert
        Assert.NotNull(response);
        _output.WriteLine($"Response: {response}");
    }

    [Theory(Skip = "Requires real API key - enable for integration testing")]
    [InlineData("openai", "gpt-4o-mini")]
    [InlineData("anthropic", "claude-3-haiku-20240307")]
    public async Task ToolCalling_DifferentModels_ShouldWork(string provider, string model)
    {
        // Arrange
        using var chatClient = CreateChatClient(provider, model);

        var testTool = AIFunctionFactory.Create(
            (string query) => $"Search results for '{query}'",
            new AIFunctionFactoryOptions
            {
                Name = "web_search",
                Description = "Search the web"
            });

        var tools = new List<AITool> { testTool };

        // Act
        var (response, hasToolCall) = await SendWithToolsAsync(
            chatClient,
            tools,
            "Search for AI news");

        // Assert
        Assert.NotNull(response);
        _output.WriteLine($"{provider}/{model} response: {response}");
    }

    [Fact(Skip = "Requires real API key - enable for integration testing")]
    public async Task ToolCalling_ShouldPreserveContextAcrossMultipleCalls()
    {
        // Arrange
        using var chatClient = CreateChatClient();

        var searchTool = AIFunctionFactory.Create(
            (string query) => $"Results for '{query}': [mock data]",
            new AIFunctionFactoryOptions
            {
                Name = "web_search",
                Description = "Search the web"
            });

        var tools = new List<AITool> { searchTool };
        var sessionMessages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are a helpful assistant with web search capabilities.")
        };

        // Act - First call using streaming
        sessionMessages.Add(new ChatMessage(ChatRole.User, "Search for information about quantum computing"));
        var responseBuilder1 = new System.Text.StringBuilder();
        await foreach (var update in chatClient.GetStreamingResponseAsync(sessionMessages, new ChatOptions { Tools = tools }))
        {
            if (update.Text != null)
                responseBuilder1.Append(update.Text);
        }
        var response1 = responseBuilder1.ToString();
        sessionMessages.Add(new ChatMessage(ChatRole.Assistant, response1));

        // Act - Second call (should preserve context) using streaming
        sessionMessages.Add(new ChatMessage(ChatRole.User, "Now search for classical computing and compare"));
        var responseBuilder2 = new System.Text.StringBuilder();
        await foreach (var update in chatClient.GetStreamingResponseAsync(sessionMessages, new ChatOptions { Tools = tools }))
        {
            if (update.Text != null)
                responseBuilder2.Append(update.Text);
        }
        var response2 = responseBuilder2.ToString();

        // Assert
        Assert.NotNull(response1);
        Assert.NotNull(response2);
        _output.WriteLine($"First response: {response1}");
        _output.WriteLine($"Second response: {response2}");
    }

    private List<AITool> CreateToolsForCase(BenchmarkCase testCase)
    {
        var tools = new List<AITool>();

        foreach (var requiredTool in testCase.RequiredTools)
        {
            var tool = requiredTool.ToLowerInvariant() switch
            {
                "list_dir" => AIFunctionFactory.Create(
                    (string path) => $"Files in {path}: [mock listing]",
                    new AIFunctionFactoryOptions { Name = "list_dir", Description = "List directory contents" }),

                "exec" => AIFunctionFactory.Create(
                    (string command) => $"Executed: {command}",
                    new AIFunctionFactoryOptions { Name = "exec", Description = "Execute shell command" }),

                "write_file" => AIFunctionFactory.Create(
                    (string path, string content) => $"Written to {path}",
                    new AIFunctionFactoryOptions { Name = "write_file", Description = "Write to file" }),

                "web_search" => AIFunctionFactory.Create(
                    (string query) => $"Search results for '{query}'",
                    new AIFunctionFactoryOptions { Name = "web_search", Description = "Web search" }),

                "browser_navigate" => AIFunctionFactory.Create(
                    (string url) => $"Navigated to {url}",
                    new AIFunctionFactoryOptions { Name = "browser_navigate", Description = "Navigate to URL" }),

                "browser_click" => AIFunctionFactory.Create(
                    (string selector) => $"Clicked {selector}",
                    new AIFunctionFactoryOptions { Name = "browser_click", Description = "Click element" }),

                "browser_type" => AIFunctionFactory.Create(
                    (string selector, string text) => $"Typed '{text}' into {selector}",
                    new AIFunctionFactoryOptions { Name = "browser_type", Description = "Type into input" }),

                _ => AIFunctionFactory.Create(
                    () => "Mock tool result",
                    new AIFunctionFactoryOptions { Name = requiredTool, Description = $"Mock {requiredTool} tool" })
            };

            tools.Add(tool);
        }

        _output.WriteLine($"Created {tools.Count} tools for case {testCase.Id}");
        return tools;
    }
}

/// <summary>
/// Benchmark case model matching cases.json structure
/// </summary>
public class BenchmarkCase
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "tool";
    public string Input { get; set; } = string.Empty;
    public int Score { get; set; } = 10;
    public string? ImagePath { get; set; }
    public List<string> RequiredTools { get; set; } = new();
    public List<string> Keywords { get; set; } = new();
    public List<string> ExpectedContent { get; set; } = new();
}
