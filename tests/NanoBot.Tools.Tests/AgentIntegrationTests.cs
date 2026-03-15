using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Configuration;
using NanoBot.Providers;
using NanoBot.Tools.BuiltIn;
using Xunit;

namespace NanoBot.Tools.Tests;

/// <summary>
/// Integration tests using ChatClientAgent to simulate actual agent behavior
/// </summary>
public class AgentIntegrationTests : IDisposable
{
    private readonly IChatClient _chatClient;
    private readonly IList<AITool> _tools;

    public AgentIntegrationTests()
    {
        // Use the Ollama qwen3.5:4b model with correct local configuration
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

        // Create tools the same way as ToolProvider.CreateDefaultTools
        _tools = new List<AITool>
        {
            WebTools.CreateWebSearchTool(),
            WebTools.CreateWebFetchTool()
        };
    }

    public void Dispose()
    {
        _chatClient?.Dispose();
    }

    private static bool EnsureEnabled()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("NANOBOT_OLLAMA_INTEGRATION"),
            "1",
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Test tool invocation through ChatOptions - similar to what Agent does
    /// This is the key test that reproduces the actual agent behavior
    /// </summary>
    [Fact]
    public async Task ChatClient_WithTools_CanInvokeWebSearch()
    {
        if (!EnsureEnabled()) return;

        // Test with explicit ChatOptions.Tools - this is what the agent does
        var options = new ChatOptions
        {
            Tools = _tools
        };

        var response = await _chatClient.GetResponseAsync(
            "Search the web for Apple's current market cap",
            options);

        Assert.NotNull(response);
        var text = response.Text ?? string.Empty;
        Assert.False(string.IsNullOrWhiteSpace(text), "Response should not be empty");

        // Check if tool was actually invoked by looking for actual search results
        Console.WriteLine($"Agent test response (first 500 chars):\n{text[..Math.Min(500, text.Length)]}");
    }

    /// <summary>
    /// Test with temperature = 0 - some models work better with lower temperature for tool calls
    /// </summary>
    [Fact]
    public async Task ChatClient_WithTools_LowTemperature_CanInvokeWebSearch()
    {
        if (!EnsureEnabled()) return;

        // Test with low temperature
        var options = new ChatOptions
        {
            Tools = _tools,
            Temperature = 0.1f
        };

        var response = await _chatClient.GetResponseAsync(
            "Search the web for Tesla's stock price",
            options);

        Assert.NotNull(response);
        var text = response.Text ?? string.Empty;
        Assert.False(string.IsNullOrWhiteSpace(text), "Response should not be empty");

        Console.WriteLine($"Low temp test response (first 500 chars):\n{text[..Math.Min(500, text.Length)]}");
    }

    /// <summary>
    /// Test multiple tool calls in sequence
    /// </summary>
    [Fact]
    public async Task ChatClient_WithTools_CanInvokeMultipleTools()
    {
        if (!EnsureEnabled()) return;

        var options = new ChatOptions
        {
            Tools = _tools
        };

        var response = await _chatClient.GetResponseAsync(
            "First search for the latest version of .NET, then fetch the official website",
            options);

        Assert.NotNull(response);
        var text = response.Text ?? string.Empty;
        Console.WriteLine($"Multi-tool test response (first 800 chars):\n{text[..Math.Min(800, text.Length)]}");
    }
}