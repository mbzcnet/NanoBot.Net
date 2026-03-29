using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
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
    private readonly string _apiKey;
    private readonly string _apiBase;
    private readonly string _provider;
    private readonly string _model;

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

        // Load configuration from environment variables
        _provider = Environment.GetEnvironmentVariable("REAL_MODEL_PROVIDER") ?? "openai";
        _model = Environment.GetEnvironmentVariable("REAL_MODEL_NAME") ?? "gpt-4o-mini";
        _apiKey = Environment.GetEnvironmentVariable($"{_provider.ToUpperInvariant()}_API_KEY") 
            ?? throw new InvalidOperationException($"{_provider.ToUpperInvariant()}_API_KEY environment variable is required");
        _apiBase = Environment.GetEnvironmentVariable($"{_provider.ToUpperInvariant()}_API_BASE");

        _output.WriteLine($"Testing with provider: {_provider}, model: {_model}");
    }

    public void Dispose()
    {
        _loggerFactory.Dispose();
    }

    private IChatClient CreateChatClient(string provider, string model)
    {
        var apiKey = Environment.GetEnvironmentVariable($"{provider.ToUpperInvariant()}_API_KEY");
        var apiBase = Environment.GetEnvironmentVariable($"{provider.ToUpperInvariant()}_API_BASE");

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException($"{provider.ToUpperInvariant()}_API_KEY environment variable is required");

        var logger = _loggerFactory.CreateLogger<ChatClientFactory>();
        var factory = new ChatClientFactory(logger);

        return factory.CreateChatClient(provider, model, apiKey, apiBase);
    }

    [Fact]
    public async Task Agent_WithRealModel_ShouldRespondToSimplePrompt()
    {
        // Arrange
        using var chatClient = CreateChatClient(_provider, _model);
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

    [Fact]
    public async Task Agent_WithRealModel_ShouldHandleMultipleTurns()
    {
        // Arrange
        using var chatClient = CreateChatClient(_provider, _model);
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

    [Fact]
    public async Task Agent_WithRealModel_ShouldFollowSystemInstructions()
    {
        // Arrange
        using var chatClient = CreateChatClient(_provider, _model);
        
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
        Assert.True(wordCount <= 10, $"Response should be concise, got {wordCount} words");
    }

    [Theory]
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

    [Fact]
    public async Task Agent_WithRealModel_ShouldHandleLongConversation()
    {
        // Arrange
        using var chatClient = CreateChatClient(_provider, _model);
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
