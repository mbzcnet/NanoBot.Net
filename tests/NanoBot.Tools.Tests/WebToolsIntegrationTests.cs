using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Configuration;
using NanoBot.Providers;
using NanoBot.Tools.BuiltIn;
using Xunit;

namespace NanoBot.Tools.Tests;

/// <summary>
/// Integration tests for web tools (web_search, web_fetch)
/// Uses Ollama qwen3.5:4b model for testing
/// </summary>
public class WebToolsIntegrationTests : IDisposable
{
    private readonly IChatClient _chatClient;

    public WebToolsIntegrationTests()
    {
        // Use the Ollama qwen3.5:4b model provided by user
        var config = new LlmConfig
        {
            DefaultProfile = "ollama_qwen3.5_4b",
            Profiles = new Dictionary<string, LlmProfile>
            {
                ["ollama_qwen3.5_4b"] = new LlmProfile
                {
                    Provider = "openai", // Ollama exposes OpenAI-compatible API
                    Model = "qwen3.5:4b",
                    ApiKey = "ollama",
                    ApiBase = "http://172.16.3.220:11435/v1"
                }
            }
        };

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        var factoryLogger = loggerFactory.CreateLogger<ChatClientFactory>();
        var factory = new ChatClientFactory(factoryLogger);

        _chatClient = factory.CreateChatClient(config);
    }

    public void Dispose()
    {
        _chatClient?.Dispose();
    }

    private static bool EnsureEnabled()
    {
        var enabled = string.Equals(
            Environment.GetEnvironmentVariable("NANOBOT_OLLAMA_INTEGRATION"),
            "1",
            StringComparison.OrdinalIgnoreCase);

        return enabled;
    }

    [Fact]
    public async Task WebSearch_WithRealLLM_CanSearchWeb()
    {
        if (!EnsureEnabled()) return;

        var webSearchTool = WebTools.CreateWebSearchTool();

        var response = await _chatClient.GetResponseAsync(
            "Search the web for Apple Inc. market cap",
            new ChatOptions { Tools = [webSearchTool] }
        );

        Assert.NotNull(response);
        var text = response.Text ?? string.Empty;
        Assert.False(string.IsNullOrWhiteSpace(text), "Response should not be empty");

        // The response should contain search results or mention of search
        Console.WriteLine($"WebSearch response: {text}");
    }

    [Fact]
    public async Task WebFetch_WithRealLLM_CanFetchWebPage()
    {
        if (!EnsureEnabled()) return;

        var webFetchTool = WebTools.CreateWebFetchTool();

        var response = await _chatClient.GetResponseAsync(
            "Fetch the content of https://example.com",
            new ChatOptions { Tools = [webFetchTool] }
        );

        Assert.NotNull(response);
        var text = response.Text ?? string.Empty;
        Assert.False(string.IsNullOrWhiteSpace(text), "Response should not be empty");

        // The response should contain some content from the page
        Console.WriteLine($"WebFetch response (first 200 chars): {text[..Math.Min(200, text.Length)]}");
    }

    [Fact]
    public async Task WebSearchAndFetch_Combined_CanUseBothTools()
    {
        if (!EnsureEnabled()) return;

        var webSearchTool = WebTools.CreateWebSearchTool();
        var webFetchTool = WebTools.CreateWebFetchTool();

        var response = await _chatClient.GetResponseAsync(
            "Search for the latest news about artificial intelligence, then fetch the first result",
            new ChatOptions { Tools = [webSearchTool, webFetchTool] }
        );

        Assert.NotNull(response);
        var text = response.Text ?? string.Empty;
        Assert.False(string.IsNullOrWhiteSpace(text), "Response should not be empty");

        Console.WriteLine($"Combined tools response (first 300 chars): {text[..Math.Min(300, text.Length)]}");
    }
}