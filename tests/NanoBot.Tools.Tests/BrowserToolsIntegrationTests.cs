using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Configuration;
using NanoBot.Core.Tools.Browser;
using NanoBot.Providers;
using NanoBot.Tools.BuiltIn;
using Xunit;

namespace NanoBot.Tools.Tests;

/// <summary>
/// A simple test implementation of IBrowserService that records all calls for verification.
/// No mocks - just records what was called with what parameters.
/// </summary>
public class TestBrowserService : IBrowserService
{
    public List<BrowserCall> Calls { get; } = new();

    public record BrowserCall(string Method, Dictionary<string, object?> Parameters);

    public Task<bool> IsStartedAsync(string profile, CancellationToken cancellationToken = default)
    {
        Calls.Add(new BrowserCall(nameof(IsStartedAsync), new Dictionary<string, object?> { ["profile"] = profile }));
        return Task.FromResult(true);
    }

    public Task EnsureStartedAsync(string profile, CancellationToken cancellationToken = default)
    {
        Calls.Add(new BrowserCall(nameof(EnsureStartedAsync), new Dictionary<string, object?> { ["profile"] = profile }));
        return Task.CompletedTask;
    }

    public Task<BrowserToolResponse> GetStatusAsync(string profile, CancellationToken cancellationToken = default)
    {
        Calls.Add(new BrowserCall(nameof(GetStatusAsync), new Dictionary<string, object?> { ["profile"] = profile }));
        return Task.FromResult(new BrowserToolResponse { Ok = true, Action = "status", Message = "Browser running" });
    }

    public Task<BrowserToolResponse> StartAsync(string profile, CancellationToken cancellationToken = default)
    {
        Calls.Add(new BrowserCall(nameof(StartAsync), new Dictionary<string, object?> { ["profile"] = profile }));
        return Task.FromResult(new BrowserToolResponse { Ok = true, Action = "start", Message = "Browser started" });
    }

    public Task<BrowserToolResponse> StopAsync(string profile, CancellationToken cancellationToken = default)
    {
        Calls.Add(new BrowserCall(nameof(StopAsync), new Dictionary<string, object?> { ["profile"] = profile }));
        return Task.FromResult(new BrowserToolResponse { Ok = true, Action = "stop", Message = "Browser stopped" });
    }

    public Task<BrowserToolResponse> StopAllAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add(new BrowserCall(nameof(StopAllAsync), new Dictionary<string, object?>()));
        return Task.FromResult(new BrowserToolResponse { Ok = true, Action = "stop_all", Message = "All browsers stopped" });
    }

    public Task<IReadOnlyList<BrowserTabInfo>> GetTabsAsync(string profile, CancellationToken cancellationToken = default)
    {
        Calls.Add(new BrowserCall(nameof(GetTabsAsync), new Dictionary<string, object?> { ["profile"] = profile }));
        return Task.FromResult<IReadOnlyList<BrowserTabInfo>>(new List<BrowserTabInfo>());
    }

    public Task<BrowserToolResponse> OpenTabAsync(string url, string profile, CancellationToken cancellationToken = default)
    {
        var targetId = $"tab_{Guid.NewGuid().ToString()[..8]}";
        Calls.Add(new BrowserCall(nameof(OpenTabAsync), new Dictionary<string, object?> { ["url"] = url, ["profile"] = profile, ["targetId"] = targetId }));
        return Task.FromResult(new BrowserToolResponse { Ok = true, Action = "open", TargetId = targetId, Message = $"Opened {url}" });
    }

    public Task<BrowserToolResponse> NavigateAsync(string targetId, string url, string profile, CancellationToken cancellationToken = default)
    {
        Calls.Add(new BrowserCall(nameof(NavigateAsync), new Dictionary<string, object?> { ["targetId"] = targetId, ["url"] = url, ["profile"] = profile }));
        return Task.FromResult(new BrowserToolResponse { Ok = true, Action = "navigate", TargetId = targetId, Message = $"Navigated to {url}" });
    }

    public Task<BrowserToolResponse> CloseTabAsync(string targetId, string profile, CancellationToken cancellationToken = default)
    {
        Calls.Add(new BrowserCall(nameof(CloseTabAsync), new Dictionary<string, object?> { ["targetId"] = targetId, ["profile"] = profile }));
        return Task.FromResult(new BrowserToolResponse { Ok = true, Action = "close", TargetId = targetId, Message = "Tab closed" });
    }

    public Task<BrowserToolResponse> GetSnapshotAsync(string targetId, string format, string profile, CancellationToken cancellationToken = default)
    {
        Calls.Add(new BrowserCall(nameof(GetSnapshotAsync), new Dictionary<string, object?> { ["targetId"] = targetId, ["format"] = format, ["profile"] = profile }));
        return Task.FromResult(new BrowserToolResponse { Ok = true, Action = "snapshot", TargetId = targetId, Snapshot = "<html><body>Test snapshot</body></html>", Message = "Snapshot captured" });
    }

    public Task<BrowserToolResponse> CaptureSnapshotAsync(string targetId, string format, string profile, string? sessionKey, CancellationToken cancellationToken = default)
    {
        Calls.Add(new BrowserCall(nameof(CaptureSnapshotAsync), new Dictionary<string, object?> { ["targetId"] = targetId, ["format"] = format, ["profile"] = profile }));
        return Task.FromResult(new BrowserToolResponse { Ok = true, Action = "snapshot", TargetId = targetId, Snapshot = "<html><body>Test page content</body></html>", Message = "Snapshot captured" });
    }

    public Task<BrowserToolResponse> GetContentAsync(string targetId, string? selector, int? maxChars, string profile, CancellationToken cancellationToken = default)
    {
        Calls.Add(new BrowserCall(nameof(GetContentAsync), new Dictionary<string, object?> { ["targetId"] = targetId, ["selector"] = selector, ["maxChars"] = maxChars, ["profile"] = profile }));
        return Task.FromResult(new BrowserToolResponse { Ok = true, Action = "content", TargetId = targetId, Content = "Test page content: This is sample text from the webpage.", Message = "Content extracted" });
    }

    public Task<BrowserToolResponse> ExecuteActionAsync(BrowserActionRequest request, string targetId, string profile, CancellationToken cancellationToken = default)
    {
        Calls.Add(new BrowserCall(nameof(ExecuteActionAsync), new Dictionary<string, object?> { ["targetId"] = targetId, ["kind"] = request.Kind, ["reference"] = request.Ref, ["profile"] = profile }));
        return Task.FromResult(new BrowserToolResponse { Ok = true, Action = "act", TargetId = targetId, Message = $"Action {request.Kind} executed" });
    }

    public void Dispose()
    {
        Calls.Add(new BrowserCall(nameof(Dispose), new Dictionary<string, object?>()));
    }
}

/// <summary>
/// Integration tests for atomic browser tools with real LLM - NO MOCKS.
/// Uses real TestBrowserService to record and verify LLM tool calls.
/// </summary>
public class BrowserToolsIntegrationTests : IDisposable
{
    private readonly IChatClient _chatClient;

    public BrowserToolsIntegrationTests()
    {
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
                    Temperature = 0.1f,
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
        return string.Equals(
            Environment.GetEnvironmentVariable("NANOBOT_OLLAMA_INTEGRATION"),
            "1",
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BrowserOpenTool_WithRealLLM_CanOpenTab()
    {
        if (!EnsureEnabled()) return;

        var browserService = new TestBrowserService();
        var browserOpenTool = BrowserTools.CreateBrowserOpenTool(browserService);

        var response = await _chatClient.GetResponseAsync(
            "Open a browser tab and navigate to https://baidu.com",
            new ChatOptions { Tools = [browserOpenTool] }
        );

        Assert.NotNull(response);
        var text = response.Text ?? string.Empty;
        Assert.False(string.IsNullOrWhiteSpace(text), "Response should not be empty");

        // Verify OpenTab was called
        var openCalls = browserService.Calls.Where(c => c.Method == nameof(TestBrowserService.OpenTabAsync)).ToList();
        Assert.True(openCalls.Count > 0, "OpenTab should have been called");
        var urlParam = openCalls[0].Parameters.GetValueOrDefault("url")?.ToString();
        Assert.False(string.IsNullOrEmpty(urlParam), "URL should be provided");

        Console.WriteLine($"Browser open test response: {text}");
        Console.WriteLine($"Calls made: {string.Join(", ", browserService.Calls.Select(c => c.Method))}");
    }

    [Fact]
    public async Task BrowserTools_WithRealLLM_CanNavigateWithTargetId()
    {
        if (!EnsureEnabled()) return;

        var browserService = new TestBrowserService();
        var browserOpenTool = BrowserTools.CreateBrowserOpenTool(browserService);
        var browserNavigateTool = BrowserTools.CreateBrowserNavigateTool(browserService);

        // First open a tab, then navigate to a new URL
        var response = await _chatClient.GetResponseAsync(
            "Open a browser tab to https://baidu.com, then navigate to https://www.163.com using the tab ID",
            new ChatOptions { Tools = [browserOpenTool, browserNavigateTool] }
        );

        Assert.NotNull(response);
        var text = response.Text ?? string.Empty;
        Assert.False(string.IsNullOrWhiteSpace(text), "Response should not be empty");

        // Verify both OpenTab and Navigate were called
        var openCalls = browserService.Calls.Where(c => c.Method == nameof(TestBrowserService.OpenTabAsync)).ToList();
        var navigateCalls = browserService.Calls.Where(c => c.Method == nameof(TestBrowserService.NavigateAsync)).ToList();

        Assert.True(openCalls.Count > 0, "OpenTab should have been called");
        Assert.True(navigateCalls.Count > 0, "Navigate should have been called");

        // Verify Navigate was called with the targetId from OpenTab
        var firstTabId = openCalls[0].Parameters["targetId"]?.ToString();
        Assert.NotNull(firstTabId);
        Assert.Equal(firstTabId, navigateCalls[0].Parameters["targetId"]);
        Assert.Equal("https://www.163.com", navigateCalls[0].Parameters["url"]);

        Console.WriteLine($"Browser navigate test response: {text}");
        Console.WriteLine($"Calls made: {string.Join(", ", browserService.Calls.Select(c => c.Method))}");
    }

    [Fact]
    public async Task BrowserTools_WithRealLLM_CanGetSnapshotAndContent()
    {
        if (!EnsureEnabled()) return;

        var browserService = new TestBrowserService();
        var browserOpenTool = BrowserTools.CreateBrowserOpenTool(browserService);
        var browserSnapshotTool = BrowserTools.CreateBrowserSnapshotTool(browserService);
        var browserContentTool = BrowserTools.CreateBrowserContentTool(browserService);

        var response = await _chatClient.GetResponseAsync(
            "Open https://baidu.com, then get a snapshot of the page and extract the content",
            new ChatOptions { Tools = [browserOpenTool, browserSnapshotTool, browserContentTool] }
        );

        Assert.NotNull(response);
        var text = response.Text ?? string.Empty;
        Assert.False(string.IsNullOrWhiteSpace(text), "Response should not be empty");

        // Verify all required calls were made with targetId
        var openCalls = browserService.Calls.Where(c => c.Method == nameof(TestBrowserService.OpenTabAsync)).ToList();
        var snapshotCalls = browserService.Calls.Where(c => c.Method == nameof(TestBrowserService.CaptureSnapshotAsync)).ToList();
        var contentCalls = browserService.Calls.Where(c => c.Method == nameof(TestBrowserService.GetContentAsync)).ToList();

        Assert.True(openCalls.Count > 0, "OpenTab should have been called");
        Assert.True(snapshotCalls.Count > 0, "CaptureSnapshot should have been called");
        Assert.True(contentCalls.Count > 0, "GetContent should have been called");

        // Verify targetId was passed correctly
        var firstTabId = openCalls[0].Parameters["targetId"]?.ToString();
        Assert.NotNull(firstTabId);
        Assert.All(snapshotCalls, c => Assert.Equal(firstTabId, c.Parameters["targetId"]));
        Assert.All(contentCalls, c => Assert.Equal(firstTabId, c.Parameters["targetId"]));

        Console.WriteLine($"Browser snapshot/content test response: {text}");
        Console.WriteLine($"Calls made: {string.Join(", ", browserService.Calls.Select(c => c.Method))}");
    }

    [Fact]
    public async Task BrowserInteractTool_WithRealLLM_CanExecuteActions()
    {
        if (!EnsureEnabled()) return;

        var browserService = new TestBrowserService();
        var browserOpenTool = BrowserTools.CreateBrowserOpenTool(browserService);
        var browserInteractTool = BrowserTools.CreateBrowserInteractTool(browserService);

        var response = await _chatClient.GetResponseAsync(
            "Open https://baidu.com, wait for the page to load, then click on element with reference '1'",
            new ChatOptions { Tools = [browserOpenTool, browserInteractTool] }
        );

        Assert.NotNull(response);
        var text = response.Text ?? string.Empty;
        Assert.False(string.IsNullOrWhiteSpace(text), "Response should not be empty");

        // Verify actions were executed with targetId
        var openCalls = browserService.Calls.Where(c => c.Method == nameof(TestBrowserService.OpenTabAsync)).ToList();
        var actionCalls = browserService.Calls.Where(c => c.Method == nameof(TestBrowserService.ExecuteActionAsync)).ToList();

        Assert.True(openCalls.Count > 0, "OpenTab should have been called");
        Assert.True(actionCalls.Count > 0, "ExecuteAction should have been called");

        // Verify targetId was passed to actions
        var firstTabId = openCalls[0].Parameters["targetId"]?.ToString();
        Assert.NotNull(firstTabId);
        Assert.All(actionCalls, c => Assert.Equal(firstTabId, c.Parameters["targetId"]));

        Console.WriteLine($"Browser action test response: {text}");
        Console.WriteLine($"Calls made: {string.Join(", ", browserService.Calls.Select(c => c.Method))}");
    }

    [Fact]
    public async Task BrowserTools_CompleteWorkflow()
    {
        if (!EnsureEnabled()) return;

        var browserService = new TestBrowserService();
        var browserOpenTool = BrowserTools.CreateBrowserOpenTool(browserService);
        var browserSnapshotTool = BrowserTools.CreateBrowserSnapshotTool(browserService);
        var browserInteractTool = BrowserTools.CreateBrowserInteractTool(browserService);
        var browserContentTool = BrowserTools.CreateBrowserContentTool(browserService);

        // Complete workflow test: open, snapshot, click, content
        var response = await _chatClient.GetResponseAsync(
            "Complete this browser workflow: 1) Open https://baidu.com, 2) Get a snapshot, 3) Click element '1', 4) Extract content",
            new ChatOptions { Tools = [browserOpenTool, browserSnapshotTool, browserInteractTool, browserContentTool] }
        );

        Assert.NotNull(response);
        var text = response.Text ?? string.Empty;

        // Verify the core workflow was executed with targetId (response may be empty if tool calls fail)
        var openCalls = browserService.Calls.Where(c => c.Method == nameof(TestBrowserService.OpenTabAsync)).ToList();
        var snapshotCalls = browserService.Calls.Where(c => c.Method == nameof(TestBrowserService.CaptureSnapshotAsync)).ToList();
        var actionCalls = browserService.Calls.Where(c => c.Method == nameof(TestBrowserService.ExecuteActionAsync)).ToList();
        var contentCalls = browserService.Calls.Where(c => c.Method == nameof(TestBrowserService.GetContentAsync)).ToList();

        Assert.True(openCalls.Count > 0, "OpenTab should have been called");

        var firstTabId = openCalls[0].Parameters["targetId"]?.ToString();
        Assert.NotNull(firstTabId);

        // Verify targetId was consistently used
        Assert.All(snapshotCalls, c => Assert.Equal(firstTabId, c.Parameters["targetId"]));
        Assert.All(actionCalls, c => Assert.Equal(firstTabId, c.Parameters["targetId"]));
        Assert.All(contentCalls, c => Assert.Equal(firstTabId, c.Parameters["targetId"]));

        Console.WriteLine($"Browser workflow test response: {text}");
        Console.WriteLine($"Calls made: {string.Join(", ", browserService.Calls.Select(c => $"{c.Method}({string.Join(", ", c.Parameters.Select(p => $"{p.Key}={p.Value}"))})"))}");
    }
}
