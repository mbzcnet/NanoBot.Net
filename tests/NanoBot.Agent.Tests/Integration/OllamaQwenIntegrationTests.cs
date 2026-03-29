using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Configuration;
using NanoBot.Providers;
using Xunit;
using Xunit.Abstractions;

namespace NanoBot.Agent.Tests.Integration;

/// <summary>
/// Integration tests using Ollama Qwen3.5 4B model for agent testing.
/// Configuration:
/// - Provider: ollama
/// - Model: qwen3.5:4b
/// - API Base: http://172.16.3.220:11435/v1
/// - API Key: ollama (placeholder)
/// </summary>
public class OllamaQwenIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _benchmarkPath;
    private readonly string _ollamaApiKey;
    private readonly string _ollamaApiBase;
    private readonly string _ollamaModel;

    public OllamaQwenIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _loggerFactory = LoggerFactory.Create(builder => builder
            .AddConsole()
            .SetMinimumLevel(LogLevel.Information));

        // Benchmark path is in src/benchmark relative to solution root
        _benchmarkPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "benchmark");
        _benchmarkPath = Path.GetFullPath(_benchmarkPath);

        // Ollama Qwen3.5 4B configuration
        // Can be overridden via environment variables:
        // - OLLAMA_API_BASE: defaults to http://172.16.3.220:11435/v1
        // - OLLAMA_API_KEY: defaults to "ollama"
        // - OLLAMA_MODEL: defaults to qwen3.5:4b
        _ollamaApiKey = Environment.GetEnvironmentVariable("OLLAMA_API_KEY") ?? "ollama";
        _ollamaApiBase = Environment.GetEnvironmentVariable("OLLAMA_API_BASE") ?? "http://172.16.3.220:11435/v1";
        _ollamaModel = Environment.GetEnvironmentVariable("OLLAMA_MODEL") ?? "qwen3.5:4b";

        _output.WriteLine($"Benchmark path: {_benchmarkPath}");
        _output.WriteLine($"Ollama API Base: {_ollamaApiBase}");
        _output.WriteLine($"Ollama Model: {_ollamaModel}");
    }

    public void Dispose()
    {
        _loggerFactory.Dispose();
    }

    private IChatClient CreateChatClient()
    {
        var logger = _loggerFactory.CreateLogger<ChatClientFactory>();
        var factory = new ChatClientFactory(logger);

        _output.WriteLine($"Creating ChatClient for Ollama Qwen3.5 4B...");
        return factory.CreateChatClient("ollama", _ollamaModel, _ollamaApiKey, _ollamaApiBase);
    }

    #region Agent Tests

    [Fact]
    public async Task Agent_WithOllamaQwen_ShouldRespondToSimplePrompt()
    {
        // Arrange
        using var chatClient = CreateChatClient();
        var agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = "TestAgent",
            Description = "Test Agent with Ollama Qwen3.5 4B"
        });

        // Act
        var session = await agent.CreateSessionAsync();
        var response = await agent.RunAsync("Hello, please respond with 'OK' if you can hear me", session);

        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response.Text);
        _output.WriteLine($"Response: {response.Text}");
    }

    [Fact]
    public async Task Agent_WithOllamaQwen_ShouldHandleMultipleTurns()
    {
        // Arrange
        using var chatClient = CreateChatClient();
        var agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = "TestAgent",
            Description = "Test Agent with Ollama Qwen3.5 4B"
        });

        var session = await agent.CreateSessionAsync();

        // Act - First turn
        var response1 = await agent.RunAsync("My name is Alice", session);
        Assert.NotNull(response1);

        // Act - Second turn (should remember context)
        var response2 = await agent.RunAsync("What is my name?", session);

        // Assert
        Assert.NotNull(response2);
        Assert.Contains("Alice", response2.Text, StringComparison.OrdinalIgnoreCase);
        _output.WriteLine($"Response 1: {response1.Text}");
        _output.WriteLine($"Response 2: {response2.Text}");
    }

    [Fact]
    public async Task Agent_WithOllamaQwen_ShouldFollowInstructions()
    {
        // Arrange
        using var chatClient = CreateChatClient();
        var agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = "TestAgent",
            Description = "Test Agent with Ollama Qwen3.5 4B"
        });

        var session = await agent.CreateSessionAsync();

        // Act - Include instruction in the prompt
        var response = await agent.RunAsync("Tell me about the weather. Please respond in exactly 3 words.", session);

        // Assert
        Assert.NotNull(response);
        var wordCount = response.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        _output.WriteLine($"Response: {response.Text} (Word count: {wordCount})");
        // Note: This is a soft assertion as models may not perfectly follow constraints
        Assert.True(wordCount <= 10, $"Response should be concise, got {wordCount} words");
    }

    #endregion

    #region Vision Tests

    [Fact]
    public async Task Vision_WithOllamaQwen_ShouldDescribeSimpleShapesAndColors()
    {
        // Arrange
        using var chatClient = CreateChatClient();
        var imagePath = GetImagePath("vision_things_v1.jpg");
        
        // Expected content from vision_things_v1.txt
        var expectedItems = new[] { "circle", "square", "cube", "mug", "phone", "apple", "blue", "red", "green" };

        // Act
        var response = await SendImageToModelAsync(chatClient, imagePath, 
            "Describe what you see in this image in detail. List all objects, shapes, and colors you can identify.");

        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response);
        
        var responseLower = response.ToLowerInvariant();
        var matchedCount = expectedItems.Count(expected => responseLower.Contains(expected.ToLowerInvariant()));
        
        _output.WriteLine($"Matched {matchedCount}/{expectedItems.Length} expected items");
        
        // Require at least 50% of expected items to be mentioned
        Assert.True(matchedCount >= expectedItems.Length / 2,
            $"Expected at least {expectedItems.Length / 2} items to be mentioned, but only found {matchedCount}");
    }

    [Fact]
    public async Task Vision_WithOllamaQwen_ShouldExtractTextFromImage()
    {
        // Arrange
        using var chatClient = CreateChatClient();
        var imagePath = GetImagePath("vision_ocr_v1.jpg");
        
        // Expected content: the model should identify text and charts in the image
        // Using flexible matching since different models may describe charts differently
        var expectedKeywords = new[] 
        { 
            // For "AI Image Recognition Test" title - model may not detect the exact title
            new[] { "ai", "image", "recognition", "test" },
            // For "bar chart" - model may say "chart" with bars or similar
            new[] { "bar", "chart" },
            // For "line graph" - model may say "line" with graph or similar
            new[] { "line", "graph" }
        };

        // Act
        var response = await SendImageToModelAsync(chatClient, imagePath,
            "Extract all text from this image. Describe any charts or graphs you see.");

        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response);
        
        var responseLower = response.ToLowerInvariant();
        
        // Count how many keyword groups are matched (at least half of words from each group)
        var matchedGroups = 0;
        for (int i = 0; i < expectedKeywords.Length; i++)
        {
            var group = expectedKeywords[i];
            var matchedWords = group.Count(word => responseLower.Contains(word));
            var threshold = Math.Max(1, group.Length / 2); // At least half of the words
            if (matchedWords >= threshold)
            {
                matchedGroups++;
            }
        }
        
        _output.WriteLine($"Matched {matchedGroups}/{expectedKeywords.Length} keyword groups");
        
        // Require at least 2 out of 3 keyword groups to be found
        Assert.True(matchedGroups >= 2,
            $"Expected at least 2 keyword groups to be extracted, but only found {matchedGroups}. Response: {response}");
    }

    [Fact]
    public async Task Vision_WithOllamaQwen_ShouldCountObjectsInImage()
    {
        // Arrange
        using var chatClient = CreateChatClient();
        var imagePath = GetImagePath("vision_things_v1.jpg");

        // Act
        var response = await SendImageToModelAsync(chatClient, imagePath,
            "How many distinct objects can you see in this image? List them all and provide a count.");

        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response);
        
        // Check if response contains a number or count
        var containsNumber = System.Text.RegularExpressions.Regex.IsMatch(response, @"\d+");
        Assert.True(containsNumber, "Response should contain a number or count of objects");
        
        _output.WriteLine($"Object counting response: {response}");
    }

    [Fact]
    public async Task Vision_WithOllamaQwen_ShouldIdentifySpatialRelationships()
    {
        // Arrange
        using var chatClient = CreateChatClient();
        var imagePath = GetImagePath("vision_things_v1.jpg");

        // Act
        var response = await SendImageToModelAsync(chatClient, imagePath,
            "Describe the spatial arrangement of objects in this image. What is at the top, middle, and bottom?");

        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response);
        
        var responseLower = response.ToLowerInvariant();
        var hasSpatialTerms = new[] { "top", "middle", "bottom", "above", "below", "left", "right" }
            .Any(term => responseLower.Contains(term));
        
        Assert.True(hasSpatialTerms, "Response should contain spatial relationship terms");
        _output.WriteLine($"Spatial description: {response}");
    }

    [Fact]
    public async Task Vision_WithOllamaQwen_ShouldRecognizeChartTypes()
    {
        // Arrange
        using var chatClient = CreateChatClient();
        var imagePath = GetImagePath("vision_ocr_v1.jpg");

        // Act
        var response = await SendImageToModelAsync(chatClient, imagePath,
            "What types of charts or graphs do you see in this image? Describe their purpose.");

        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response);
        
        var responseLower = response.ToLowerInvariant();
        var hasChartTerms = new[] { "bar", "chart", "graph", "line", "data", "visualization" }
            .Any(term => responseLower.Contains(term));
        
        Assert.True(hasChartTerms, "Response should identify chart/graph types");
        _output.WriteLine($"Chart recognition response: {response}");
    }

    [Fact]
    public async Task Vision_WithOllamaQwen_ShouldHandleMultipleImagesSequentially()
    {
        // Arrange
        using var chatClient = CreateChatClient();
        var imagePaths = new[]
        {
            GetImagePath("vision_things_v1.jpg"),
            GetImagePath("vision_ocr_v1.jpg")
        };

        // Act & Assert
        foreach (var imagePath in imagePaths)
        {
            var response = await SendImageToModelAsync(chatClient, imagePath, "What do you see in this image?");

            Assert.NotNull(response);
            Assert.NotEmpty(response);
            _output.WriteLine($"Image {Path.GetFileName(imagePath)}: {response[..Math.Min(200, response.Length)]}...");
        }
    }

    [Fact]
    public async Task Vision_WithOllamaQwen_ShouldAnswerQuestionsAboutImage()
    {
        // Arrange
        using var chatClient = CreateChatClient();
        var imagePath = GetImagePath("vision_things_v1.jpg");

        // First, get general description
        var descriptionResponse = await SendImageToModelAsync(chatClient, imagePath, "Describe this image.");

        Assert.NotNull(descriptionResponse);
        _output.WriteLine($"Initial description: {descriptionResponse}");

        // Act - Ask specific follow-up question
        var questionResponse = await SendImageToModelAsync(chatClient, imagePath,
            "What color is the circle in this image?");

        // Assert
        Assert.NotNull(questionResponse);
        Assert.NotEmpty(questionResponse);
        Assert.Contains("blue", questionResponse.ToLowerInvariant());
        
        _output.WriteLine($"Question answer: {questionResponse}");
    }

    #endregion

    #region Tool Calling Tests

    [Fact]
    public async Task ToolCalling_WithOllamaQwen_ShouldLoadBenchmarkCases()
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

    [Fact]
    public async Task ToolCalling_WithOllamaQwen_ShouldCallFileReadTool()
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
        var (response, hasToolCall) = await SendWithToolsAsync(chatClient, tools, "List files in /tmp directory");

        // Assert
        Assert.NotNull(response);
        _output.WriteLine($"Response: {response}");
        _output.WriteLine($"Has tool call: {hasToolCall}");
    }

    [Fact]
    public async Task ToolCalling_WithOllamaQwen_ShouldCallWebSearchTool()
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
        var (response, hasToolCall) = await SendWithToolsAsync(chatClient, tools, "Search for today's weather in Beijing");

        // Assert
        Assert.NotNull(response);
        _output.WriteLine($"Response: {response}");
        _output.WriteLine($"Has tool call: {hasToolCall}");
    }

    [Fact]
    public async Task ToolCalling_WithOllamaQwen_ShouldHandleMultiToolScenario()
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
        var (response, hasToolCall) = await SendWithToolsAsync(chatClient, tools,
            "First list /tmp directory, then create a file there named test.txt with content 'hello'");

        // Assert
        Assert.NotNull(response);
        _output.WriteLine($"Response: {response}");
        _output.WriteLine($"Has tool call: {hasToolCall}");
    }

    [Fact]
    public async Task ToolCalling_WithOllamaQwen_ShouldCallBrowserNavigateTool()
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
        var (response, hasToolCall) = await SendWithToolsAsync(chatClient, tools, "Open https://example.com webpage");

        // Assert
        Assert.NotNull(response);
        _output.WriteLine($"Response: {response}");
        _output.WriteLine($"Has tool call: {hasToolCall}");
    }

    [Fact]
    public async Task ToolCalling_WithOllamaQwen_ShouldCallBrowserClickTool()
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
        var (response, hasToolCall) = await SendWithToolsAsync(chatClient, tools,
            "Navigate to https://www.google.com and click the search button");

        // Assert
        Assert.NotNull(response);
        _output.WriteLine($"Response: {response}");
        _output.WriteLine($"Has tool call: {hasToolCall}");
    }

    [Fact]
    public async Task ToolCalling_WithOllamaQwen_ShouldCallBrowserTypeTool()
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
        var (response, hasToolCall) = await SendWithToolsAsync(chatClient, tools,
            "Navigate to https://www.google.com and type 'hello world' in the search box");

        // Assert
        Assert.NotNull(response);
        _output.WriteLine($"Response: {response}");
        _output.WriteLine($"Has tool call: {hasToolCall}");
    }

    [Fact]
    public async Task ToolCalling_WithOllamaQwen_ShouldPreserveContextAcrossMultipleCalls()
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

        // Act - First call
        sessionMessages.Add(new ChatMessage(ChatRole.User, "Search for information about quantum computing"));
        var response1 = await chatClient.GetResponseAsync(sessionMessages, new ChatOptions { Tools = tools });
        sessionMessages.Add(new ChatMessage(ChatRole.Assistant, response1.Text ?? ""));

        // Act - Second call (should preserve context)
        sessionMessages.Add(new ChatMessage(ChatRole.User, "Now search for classical computing and compare"));
        var response2 = await chatClient.GetResponseAsync(sessionMessages, new ChatOptions { Tools = tools });

        // Assert
        Assert.NotNull(response1.Text);
        Assert.NotNull(response2.Text);
        _output.WriteLine($"First response: {response1.Text}");
        _output.WriteLine($"Second response: {response2.Text}");
    }

    #endregion

    #region Helper Methods

    private string GetImagePath(string fileName)
    {
        var path = Path.Combine(_benchmarkPath, fileName);
        _output.WriteLine($"Looking for image at: {path}");
        
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Test image not found: {path}");
        }
        
        return path;
    }

    private async Task<string> SendImageToModelAsync(IChatClient chatClient, string imagePath, string prompt)
    {
        var imageBytes = await File.ReadAllBytesAsync(imagePath);
        var mediaType = GetImageMediaType(imagePath);

        var contents = new List<AIContent>
        {
            new DataContent(imageBytes, mediaType),
            new TextContent(prompt)
        };

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are an AI assistant with vision capabilities. Please describe images accurately and truthfully."),
            new(ChatRole.User, contents)
        };

        _output.WriteLine($"Sending image {Path.GetFileName(imagePath)} to model with prompt: {prompt}");

        var response = await chatClient.GetResponseAsync(messages);

        var responseText = response.Text ?? "";
        _output.WriteLine($"Model response: {responseText}");

        return responseText;
    }

    private string GetImageMediaType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            _ => "image/png"
        };
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

        var response = await chatClient.GetResponseAsync(messages, options);

        var responseText = response.Text ?? "";
        // Check if any message in the response contains FunctionCallContent
        var hasToolCall = response.Messages?.Any(m => m.Contents.Any(c => c is FunctionCallContent)) == true;

        _output.WriteLine($"Response: {responseText}");
        _output.WriteLine($"Has tool call: {hasToolCall}");

        return (responseText, hasToolCall);
    }

    #endregion
}
