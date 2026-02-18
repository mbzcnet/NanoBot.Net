using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using NanoBot.Core.Configuration;
using Xunit;

namespace NanoBot.Providers.Tests;

public class ChatClientFactoryTests
{
    private readonly Mock<ILogger<ChatClientFactory>> _loggerMock;
    private readonly ChatClientFactory _factory;

    public ChatClientFactoryTests()
    {
        _loggerMock = new Mock<ILogger<ChatClientFactory>>();
        _factory = new ChatClientFactory(_loggerMock.Object);
    }

    [Fact]
    public void CreateChatClient_WithUnsupportedProvider_ThrowsNotSupportedException()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            _factory.CreateChatClient("unsupported-provider", "model", "key"));

        Assert.Contains("unsupported-provider", ex.Message);
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("anthropic")]
    [InlineData("deepseek")]
    [InlineData("groq")]
    [InlineData("moonshot")]
    [InlineData("zhipu")]
    [InlineData("openrouter")]
    [InlineData("ollama")]
    public void CreateChatClient_SupportedProviders_WithApiKey_ReturnsIChatClient(string provider)
    {
        var client = _factory.CreateChatClient(provider, "test-model", "test-api-key");

        Assert.NotNull(client);
        Assert.IsAssignableFrom<IChatClient>(client);
    }

    [Fact]
    public void CreateChatClient_WithCustomApiBase_ReturnsIChatClient()
    {
        var client = _factory.CreateChatClient(
            "openai",
            "gpt-4o",
            "test-key",
            "https://custom-api.example.com/v1");

        Assert.NotNull(client);
    }

    [Fact]
    public void CreateChatClient_WithLlmConfig_ReturnsIChatClient()
    {
        var config = new LlmConfig
        {
            Provider = "ollama",
            Model = "llama3.2",
            ApiBase = "http://localhost:11434/v1",
            ApiKey = "ollama"
        };

        var client = _factory.CreateChatClient(config);

        Assert.NotNull(client);
    }
}
