using Microsoft.Extensions.Logging;
using NanoBot.Core.Benchmark;

namespace NanoBot.Providers.Benchmark;

public class BenchmarkEngine : IBenchmarkEngine
{
    private readonly ILogger<BenchmarkEngine> _logger;

    public BenchmarkEngine(ILogger<BenchmarkEngine> logger)
    {
        _logger = logger;
    }

    public async Task<BenchmarkResult> RunBenchmarkAsync(
        object chatClient,
        IReadOnlyList<object> tools,
        CancellationToken cancellationToken = default)
    {
        var client = chatClient as Microsoft.Extensions.AI.IChatClient
            ?? throw new ArgumentException("chatClient must be of type IChatClient");

        var result = new BenchmarkResult
        {
            Model = "model",
            Provider = "provider",
            MaxScore = GetDefaultCases().Sum(c => c.Score)
        };

        _logger.LogInformation("Starting benchmark for model");

        foreach (var testCase in GetDefaultCases())
        {
            var caseResult = await EvaluateCaseAsync(client, testCase, cancellationToken);
            result.CaseResults.Add(caseResult);

            if (caseResult.Passed)
            {
                result.TotalScore += caseResult.Score;
            }

            _logger.LogInformation("Case {CaseId}: {Passed}", caseResult.CaseId, caseResult.Passed);
        }

        // Evaluate capabilities
        result.Capabilities = EvaluateCapabilities(result);
        result.Capabilities.LastBenchmarkTime = result.Timestamp;

        return result;
    }

    private async Task<CaseResult> EvaluateCaseAsync(
        Microsoft.Extensions.AI.IChatClient client,
        BenchmarkCase testCase,
        CancellationToken cancellationToken)
    {
        var result = new CaseResult
        {
            CaseId = testCase.Id,
            CaseName = testCase.Name
        };

        try
        {
            // Simple text completion test
            var messages = new List<Microsoft.Extensions.AI.ChatMessage>
            {
                new(Microsoft.Extensions.AI.ChatRole.System, "你是一个AI助手。"),
                new(Microsoft.Extensions.AI.ChatRole.User, testCase.Input)
            };

            var response = await client.GetResponseAsync(messages, cancellationToken: cancellationToken);

            var responseText = string.Join(" ", response.Messages.Select(m => m.ToString()));
            result.ActualTools = responseText.Length > 100 ? responseText[..100] + "..." : responseText;

            // For now, we just check if the model responds with a non-empty message
            // In the future, we can enhance this to check for tool calls in the response
            result.Passed = !string.IsNullOrWhiteSpace(responseText);
            result.Score = result.Passed ? testCase.Score : 0;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            result.Passed = false;
            result.Score = 0;
            _logger.LogError(ex, "Error evaluating case {CaseId}", testCase.Id);
        }

        return result;
    }

    private ModelCapabilities EvaluateCapabilities(BenchmarkResult result)
    {
        return new ModelCapabilities
        {
            // If we can get a response, assume tool calling is supported (for now)
            SupportsToolCalling = result.CaseResults.Any(c => c.Passed),
            SupportsVision = false
        };
    }

    private static List<BenchmarkCase> GetDefaultCases()
    {
        return new List<BenchmarkCase>
        {
            new() { Id = "tool_file_read", Name = "文件读取", Input = "帮我看看 /tmp 目录里有什么", RequiredTools = new List<string> { "list_dir" }, Score = 10 },
            new() { Id = "tool_file_write", Name = "文件写入", Input = "在 /tmp 目录创建 test.txt，内容为 'hello'", RequiredTools = new List<string> { "write_file" }, Score = 10 },
            new() { Id = "tool_shell", Name = "Shell命令", Input = "执行 pwd 命令", RequiredTools = new List<string> { "exec" }, Score = 10 },
            new() { Id = "tool_web_search", Name = "网页搜索", Input = "搜索今天的天气", RequiredTools = new List<string> { "web_search" }, Score = 10 },
            new() { Id = "tool_multi", Name = "多工具调用", Input = "先列出 /tmp 目录，然后在那里创建 test.txt", RequiredTools = new List<string> { "list_dir", "write_file" }, Score = 15 },
            new() { Id = "browser_navigate", Name = "浏览器导航", Input = "打开 https://example.com 网页", RequiredTools = new List<string> { "browser_navigate" }, Score = 10 },
            new() { Id = "browser_click", Name = "浏览器点击", Input = "点击页面上的搜索按钮", RequiredTools = new List<string> { "browser_click" }, Score = 10 },
            new() { Id = "browser_type", Name = "浏览器输入", Input = "在搜索框输入 'hello world'", RequiredTools = new List<string> { "browser_type" }, Score = 10 },
            new() { Id = "browser_screenshot", Name = "浏览器截图", Input = "截取当前页面", RequiredTools = new List<string> { "browser_screenshot" }, Score = 10 },
            new() { Id = "browser_content", Name = "获取页面内容", Input = "获取当前页面的内容", RequiredTools = new List<string> { "browser_get_content" }, Score = 10 }
        };
    }
}