using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Configuration;
using NanoBot.Providers;
using NanoBot.Tools.BuiltIn;
using Xunit;

namespace NanoBot.Tools.Tests;

/// <summary>
/// Integration tests for web_page tool
/// Uses Ollama qwen3.5:4b model for testing
/// </summary>
public class WebToolsIntegrationTests : IDisposable
{
    private readonly IChatClient _chatClient;

    public WebToolsIntegrationTests()
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
    public async Task WebPage_Search_WithRealLLM_CanSearchWeb()
    {
        if (!EnsureEnabled()) return;

        var webPageTool = WebTools.CreateWebPageTool();

        var response = await _chatClient.GetResponseAsync(
            "Search the web for Apple Inc. market cap",
            new ChatOptions { Tools = [webPageTool] }
        );

        Assert.NotNull(response);
        var text = response.Text ?? string.Empty;
        Assert.False(string.IsNullOrWhiteSpace(text), "Response should not be empty");

        // The response should contain search results or mention of search
        Console.WriteLine($"WebPage search response: {text}");
    }

    [Fact]
    public async Task WebPage_Fetch_WithRealLLM_CanFetchWebPage()
    {
        if (!EnsureEnabled()) return;

        var webPageTool = WebTools.CreateWebPageTool();

        var response = await _chatClient.GetResponseAsync(
            "Fetch the content of https://example.com",
            new ChatOptions { Tools = [webPageTool] }
        );

        Assert.NotNull(response);
        var text = response.Text ?? string.Empty;
        Assert.False(string.IsNullOrWhiteSpace(text), "Response should not be empty");

        // The response should contain some content from the page
        Console.WriteLine($"WebPage fetch response (first 200 chars): {text[..Math.Min(200, text.Length)]}");
    }

    [Fact]
    public async Task WebPage_Combined_WithRealLLM_CanUseBothModes()
    {
        if (!EnsureEnabled()) return;

        var webPageTool = WebTools.CreateWebPageTool();

        var response = await _chatClient.GetResponseAsync(
            "Search for the latest news about artificial intelligence",
            new ChatOptions { Tools = [webPageTool] }
        );

        Assert.NotNull(response);
        var text = response.Text ?? string.Empty;
        Assert.False(string.IsNullOrWhiteSpace(text), "Response should not be empty");

        Console.WriteLine($"WebPage combined response (first 300 chars): {text[..Math.Min(300, text.Length)]}");
    }
}
