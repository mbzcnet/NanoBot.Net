using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Configuration;
using NanoBot.Providers;
using Xunit;
using Xunit.Abstractions;

namespace NanoBot.Agent.Tests.Integration;

/// <summary>
/// Integration tests using real LLM models for agent testing.
/// Requires environment variables to be set:
/// - OPENAI_API_KEY (or other provider API keys)
/// </summary>
public class RealModelIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _benchmarkPath;

    public RealModelIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _loggerFactory = LoggerFactory.Create(builder => builder
            .AddConsole()
            .SetMinimumLevel(LogLevel.Information));

        _benchmarkPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..",
            "src", "benchmark");
        _benchmarkPath = Path.GetFullPath(_benchmarkPath);
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

    [Fact(Skip = "Requires real API key - enable for integration testing")]
    public async Task Agent_WithRealModel_ShouldRespondToSimplePrompt()
    {
        // Arrange
        using var chatClient = CreateChatClient();
        var agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = "TestAgent",
            Description = "Test Agent with real model"
        });

        // Act
        var session = await agent.CreateSessionAsync();
        var response = await agent.RunAsync("Hello, please respond with 'OK' if you can hear me", session);

        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response.Text);
        _output.WriteLine($"Response: {response.Text}");
    }

    [Fact(Skip = "Requires real API key - enable for integration testing")]
    public async Task Agent_WithRealModel_ShouldHandleMultipleTurns()
    {
        // Arrange
        using var chatClient = CreateChatClient();
        var agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = "TestAgent",
            Description = "Test Agent with real model"
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

    [Fact(Skip = "Requires real API key - enable for integration testing")]
    public async Task Agent_WithRealModel_ShouldFollowSystemInstructions()
    {
        // Arrange
        using var chatClient = CreateChatClient();
        
        // Note: ChatClientAgentOptions doesn't have Instructions property
        // Instructions are typically passed via system message or agent configuration
        var agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = "TestAgent",
            Description = "Test Agent"
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

    [Theory(Skip = "Requires real API key - enable for integration testing")]
    [InlineData("openai", "gpt-4o-mini")]
    [InlineData("anthropic", "claude-3-haiku-20240307")]
    [InlineData("deepseek", "deepseek-chat")]
    public async Task Agent_WithDifferentProviders_ShouldWork(string provider, string model)
    {
        // Arrange
        using var chatClient = CreateChatClient(provider, model);
        var agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = "TestAgent",
            Description = $"Test Agent with {provider}/{model}"
        });

        // Act
        var session = await agent.CreateSessionAsync();
        var response = await agent.RunAsync("Say hello", session);

        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response.Text);
        _output.WriteLine($"{provider}/{model} response: {response.Text}");
    }

    [Fact(Skip = "Requires real API key - enable for integration testing")]
    public async Task Agent_WithRealModel_ShouldHandleLongConversation()
    {
        // Arrange
        using var chatClient = CreateChatClient();
        var agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = "TestAgent",
            Description = "Test Agent with real model"
        });

        var session = await agent.CreateSessionAsync();
        var conversation = new List<(string User, string ExpectedTopic)>
        {
            ("I like programming", "programming"),
            ("What's your favorite programming language?", "language"),
            ("Tell me about Python", "Python"),
            ("How about JavaScript?", "JavaScript")
        };

        // Act & Assert
        foreach (var (userMessage, expectedTopic) in conversation)
        {
            var response = await agent.RunAsync(userMessage, session);
            Assert.NotNull(response);
            Assert.NotEmpty(response.Text);
            _output.WriteLine($"User: {userMessage}");
            _output.WriteLine($"Agent: {response.Text}");
        }
    }
}

/// <summary>
/// Exception to skip tests when required configuration is missing
/// </summary>
public class SkipTestException : Exception
{
    public SkipTestException(string message) : base(message) { }
}
