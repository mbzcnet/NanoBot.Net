using MartinCostello.Logging.XUnit;
using Microsoft.Extensions.Logging;
using NanoBot.Providers;
using Xunit;
using Xunit.Abstractions;

namespace NanoBot.Integration.Tests;

/// <summary>
/// 测试 Ollama 的 OpenAI 兼容 API 调用
/// 这些测试需要真实的 Ollama 服务运行
/// </summary>
public class OllamaOpenAICompatibilityTests
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger<ChatClientFactory> _logger;

    // Ollama 服务地址
    private const string OllamaBaseUrl = "http://172.16.3.220:11435";

    public OllamaOpenAICompatibilityTests(ITestOutputHelper output)
    {
        _output = output;
        _logger = output.ToLogger<ChatClientFactory>();
    }

    /// <summary>
    /// 测试使用 Provider=openai + Ollama ApiBase 的方式调用
    /// 这是用户当前的配置方式，模拟 OpenAI SDK 访问 Ollama 的兼容端点
    /// </summary>
    [Fact(Skip = "Requires real Ollama service")]
    public async Task OpenAI_Provider_With_Ollama_ApiBase_ShouldWork()
    {
        // 模拟用户配置：Provider=openai, ApiBase=Ollama地址
        var factory = new ChatClientFactory(_logger);

        _output.WriteLine($"Testing OpenAI provider with Ollama base URL: {OllamaBaseUrl}");

        // 创建客户端 - 使用 openai provider 但指向 Ollama 地址
        var client = factory.CreateChatClient(
            provider: "openai",
            model: "qwen3.5:4b",  // Ollama 模型名称
            apiKey: "ollama",      // Ollama 需要任意 API key
            apiBase: $"{OllamaBaseUrl}/v1");  // OpenAI 兼容端点需要 /v1 后缀

        Assert.NotNull(client);

        // 尝试发送简单请求
        var messages = new List<Microsoft.Extensions.AI.ChatMessage>
        {
            new(Microsoft.Extensions.AI.ChatRole.User, "Hello, this is a test message. Please respond with 'OK'.")
        };

        var response = await client.GetResponseAsync(messages);

        Assert.NotNull(response);
        Assert.False(string.IsNullOrEmpty(response.Text), "Response should not be empty");
        _output.WriteLine($"Response: {response.Text}");
    }

    /// <summary>
    /// 测试使用 Provider=ollama 的方式调用
    /// 这是正确的配置方式
    /// </summary>
    [Fact(Skip = "Requires real Ollama service")]
    public async Task Ollama_Provider_ShouldWork()
    {
        var factory = new ChatClientFactory(_logger);

        _output.WriteLine($"Testing Ollama provider with base URL: {OllamaBaseUrl}");

        // 创建客户端 - 使用 ollama provider
        var client = factory.CreateChatClient(
            provider: "ollama",
            model: "qwen3.5:4b",
            apiKey: null,  // Ollama 不需要 API key
            apiBase: OllamaBaseUrl);  // 不需要 /v1 后缀

        Assert.NotNull(client);

        // 尝试发送简单请求
        var messages = new List<Microsoft.Extensions.AI.ChatMessage>
        {
            new(Microsoft.Extensions.AI.ChatRole.User, "Hello, this is a test message. Please respond with 'OK'.")
        };

        var response = await client.GetResponseAsync(messages);

        Assert.NotNull(response);
        Assert.False(string.IsNullOrEmpty(response.Text), "Response should not be empty");
        _output.WriteLine($"Response: {response.Text}");
    }

    /// <summary>
    /// 验证 Ollama 的模型列表 API 是否可访问
    /// </summary>
    [Fact]
    public async Task Ollama_Models_Api_ShouldBeAccessible()
    {
        using var httpClient = new HttpClient();
        httpClient.BaseAddress = new Uri(OllamaBaseUrl);
        httpClient.Timeout = TimeSpan.FromSeconds(10);

        // Ollama 原生 API 获取模型列表
        var response = await httpClient.GetAsync("/api/tags");

        _output.WriteLine($"Native API response status: {response.StatusCode}");

        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            _output.WriteLine($"Models available: {content}");
        }

        // OpenAI 兼容 API 获取模型列表
        var openAiResponse = await httpClient.GetAsync("/v1/models");

        _output.WriteLine($"OpenAI compatible API response status: {openAiResponse.StatusCode}");

        if (openAiResponse.IsSuccessStatusCode)
        {
            var content = await openAiResponse.Content.ReadAsStringAsync();
            _output.WriteLine($"Models via OpenAI API: {content}");
        }
    }

    /// <summary>
    /// 测试不同模型名称格式
    /// </summary>
    [Theory(Skip = "Requires real Ollama service")]
    [InlineData("qwen3.5:4b")]
    [InlineData("llama3.2")]
    public async Task Different_Model_Formats_ShouldWork(string modelName)
    {
        var factory = new ChatClientFactory(_logger);

        _output.WriteLine($"Testing model: {modelName}");

        var client = factory.CreateChatClient(
            provider: "ollama",
            model: modelName,
            apiKey: null,
            apiBase: OllamaBaseUrl);

        Assert.NotNull(client);

        var messages = new List<Microsoft.Extensions.AI.ChatMessage>
        {
            new(Microsoft.Extensions.AI.ChatRole.User, "Say 'test' and nothing else.")
        };

        var response = await client.GetResponseAsync(messages);

        Assert.NotNull(response);
        _output.WriteLine($"Response from {modelName}: {response.Text}");
    }
}
