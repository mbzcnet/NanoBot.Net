using System.ComponentModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using NanoBot.Core.Configuration;
using NanoBot.Core.Tools.Browser;
using NanoBot.Providers;
using NanoBot.Tools.BuiltIn;

namespace NanoBot.Tools.Tests.Temp;

/// <summary>
/// Standalone integration test for BrowserTools with real LLM
/// Tests that LLM correctly understands and invokes browser tool with proper parameters
/// </summary>
public class BrowserToolsIntegrationTest
{
    private readonly IChatClient _chatClient;
    private readonly Mock<IBrowserService> _mockBrowserService;

    public BrowserToolsIntegrationTest()
    {
        // Use the Ollama qwen3.5:4b model
        var config = new LlmConfig
        {
            DefaultProfile = "ollama_qwen3.5_4b",
            Profiles = new Dictionary<string, LlmProfile>
            {
                ["ollama_qwen3.5_4b"] = new LlmProfile
                {
                    Provider = "openai",
                    Model = "qwen3.5:4b",
                    ApiKey = "ollama",
                    ApiBase = "http://172.16.3.220:11435/v1",
                    Temperature = 0.1f // Lower temp for more deterministic tool calls
                }
            }
        };

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        var factoryLogger = loggerFactory.CreateLogger<ChatClientFactory>();
        var factory = new ChatClientFactory(factoryLogger);

        _chatClient = factory.CreateChatClient(config);

        // Setup mock browser service
        _mockBrowserService = new Mock<IBrowserService>();
        SetupMockBrowserService();
    }

    private void SetupMockBrowserService()
    {
        _mockBrowserService
            .Setup(x => x.GetStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BrowserToolResponse { Ok = true, Action = "status", Message = "Browser is running" });

        _mockBrowserService
            .Setup(x => x.StartAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BrowserToolResponse { Ok = true, Action = "start", Message = "Browser started" });

        _mockBrowserService
            .Setup(x => x.OpenTabAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string url, string profile, CancellationToken _) => new BrowserToolResponse
            {
                Ok = true,
                Action = "open",
                TargetId = "tab_12345",
                Message = $"Opened {url}"
            });

        _mockBrowserService
            .Setup(x => x.NavigateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string targetId, string url, string profile, CancellationToken _) => new BrowserToolResponse
            {
                Ok = true,
                Action = "navigate",
                TargetId = targetId,
                Message = $"Navigated to {url}"
            });

        _mockBrowserService
            .Setup(x => x.CaptureSnapshotAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string targetId, string format, string profile, string sessionKey, CancellationToken _) => new BrowserToolResponse
            {
                Ok = true,
                Action = "snapshot",
                TargetId = targetId,
                Snapshot = "<mock snapshot content>",
                Message = "Snapshot captured"
            });

        _mockBrowserService
            .Setup(x => x.GetContentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string targetId, string selector, int? maxChars, string profile, CancellationToken _) => new BrowserToolResponse
            {
                Ok = true,
                Action = "content",
                TargetId = targetId,
                Content = "Mock page content: Welcome to the test page.",
                Message = "Content extracted"
            });

        _mockBrowserService
            .Setup(x => x.ExecuteActionAsync(It.IsAny<BrowserActionRequest>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BrowserActionRequest req, string targetId, string profile, CancellationToken _) => new BrowserToolResponse
            {
                Ok = true,
                Action = "act",
                TargetId = targetId,
                Message = $"Action {req.Kind} executed"
            });

        _mockBrowserService
            .Setup(x => x.CloseTabAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string targetId, string profile, CancellationToken _) => new BrowserToolResponse
            {
                Ok = true,
                Action = "close",
                TargetId = targetId,
                Message = "Tab closed"
            });

        _mockBrowserService
            .Setup(x => x.StopAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BrowserToolResponse { Ok = true, Action = "stop", Message = "Browser stopped" });
    }

    public async Task RunTestAsync()
    {
        var browserTool = BrowserTools.CreateBrowserTool(_mockBrowserService.Object);

        Console.WriteLine("=== Test 1: Open browser tab ===");
        var response1 = await _chatClient.GetResponseAsync(
            "Open a browser tab and navigate to https://example.com",
            new ChatOptions { Tools = [browserTool] }
        );
        Console.WriteLine($"Response: {response1.Text}");
        Console.WriteLine();

        Console.WriteLine("=== Test 2: Navigate with targetId ===");
        _mockBrowserService.Invocations.Clear();
        var response2 = await _chatClient.GetResponseAsync(
            "Open a browser tab to https://example.com, then navigate to https://google.com using the tab ID tab_12345",
            new ChatOptions { Tools = [browserTool] }
        );
        Console.WriteLine($"Response: {response2.Text}");
        Console.WriteLine();

        Console.WriteLine("=== Test 3: Complete workflow ===");
        _mockBrowserService.Invocations.Clear();
        var response3 = await _chatClient.GetResponseAsync(
            "Complete this browser workflow: 1) Open https://example.com, 2) Get a snapshot using tab_12345, 3) Extract content, 4) Close the tab",
            new ChatOptions { Tools = [browserTool] }
        );
        Console.WriteLine($"Response: {response3.Text}");
    }
}

public class Program
{
    public static async Task Main(string[] args)
    {
        if (!IsIntegrationEnabled())
        {
            Console.WriteLine("Integration tests disabled. Set NANOBOT_OLLAMA_INTEGRATION=1 to enable.");
            return;
        }

        var test = new BrowserToolsIntegrationTest();
        await test.RunTestAsync();
    }

    private static bool IsIntegrationEnabled()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("NANOBOT_OLLAMA_INTEGRATION"),
            "1",
            StringComparison.OrdinalIgnoreCase);
    }
}
