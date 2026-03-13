using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Benchmark;

namespace NanoBot.Providers.Benchmark;

public class BenchmarkEngine : IBenchmarkEngine
{
    private readonly ILogger<BenchmarkEngine> _logger;
    private readonly string _workspacePath;
    private readonly string _benchmarkPath;

    private const int ToolMaxScore = 80;
    private const int VisionMaxScore = 20;

    public BenchmarkEngine(ILogger<BenchmarkEngine> logger)
    {
        _logger = logger;
        _workspacePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nbot", "workspace");
        _benchmarkPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..",
            "benchmark");
        // Normalize path
        _benchmarkPath = Path.GetFullPath(_benchmarkPath);
    }

    public async Task<BenchmarkResult> RunBenchmarkAsync(
        object chatClient,
        IReadOnlyList<object> tools,
        CancellationToken cancellationToken = default)
    {
        var result = new BenchmarkResult
        {
            Timestamp = DateTime.UtcNow
        };

        var cases = LoadTestCases();
        var toolCases = cases.Where(c => c.Category == "tool").ToList();
        var visionCases = cases.Where(c => c.Category == "vision").ToList();

        _logger.LogInformation("Starting benchmark - {ToolCount} tool cases, {VisionCount} vision cases",
            toolCases.Count, visionCases.Count);
        _logger.LogInformation("Benchmark path: {Path}", _benchmarkPath);
        _logger.LogInformation("Workspace path: {Path}", _workspacePath);

        // Run tool tests

        // Run tool tests
        foreach (var testCase in toolCases)
        {
            var caseResult = await EvaluateToolCaseAsync(testCase, cancellationToken);
            caseResult.Category = "tool";
            result.CaseResults.Add(caseResult);

            if (caseResult.Passed)
            {
                result.TotalScore += caseResult.Score;
                result.ToolPassCount++;
            }
            result.ToolTotalCount++;

            _logger.LogInformation("Tool case {CaseId}: {Passed}", caseResult.CaseId, caseResult.Passed);
        }

        // Run vision tests
        foreach (var testCase in visionCases)
        {
            var caseResult = await EvaluateVisionCaseAsync(testCase, cancellationToken);
            caseResult.Category = "vision";
            result.CaseResults.Add(caseResult);

            if (caseResult.Passed)
            {
                result.TotalScore += caseResult.Score;
                result.VisionPassCount++;
            }
            result.VisionTotalCount++;

            _logger.LogInformation("Vision case {CaseId}: {Passed}", caseResult.CaseId, caseResult.Passed);
        }

        // Calculate 100-point scores
        result.ToolScore = result.ToolTotalCount > 0
            ? (int)Math.Round((double)result.ToolPassCount / result.ToolTotalCount * ToolMaxScore)
            : 0;
        result.VisionScore = result.VisionTotalCount > 0
            ? (int)Math.Round((double)result.VisionPassCount / result.VisionTotalCount * VisionMaxScore)
            : 0;

        result.MaxScore = ToolMaxScore + VisionMaxScore;

        // Evaluate capabilities
        result.Capabilities = EvaluateCapabilities(result);
        result.Capabilities.LastBenchmarkTime = result.Timestamp;
        result.Capabilities.Score = result.FinalScore;
        result.Capabilities.ToolScore = result.ToolScore;
        result.Capabilities.VisionScore = result.VisionScore;

        return result;
    }

    private List<BenchmarkCase> LoadTestCases()
    {
        var casesFile = Path.Combine(_benchmarkPath, "cases.json");

        if (!File.Exists(casesFile))
        {
            _logger.LogWarning("Test cases file not found: {Path}, using default cases", casesFile);
            return GetDefaultCases();
        }

        try
        {
            var json = File.ReadAllText(casesFile);
            var doc = JsonDocument.Parse(json);
            var cases = new List<BenchmarkCase>();

            // Load tool cases
            if (doc.RootElement.TryGetProperty("tool", out var toolCases))
            {
                foreach (var caseElement in toolCases.EnumerateArray())
                {
                    var benchmarkCase = new BenchmarkCase
                    {
                        Id = caseElement.GetProperty("id").GetString() ?? "",
                        Name = caseElement.GetProperty("name").GetString() ?? "",
                        Category = "tool",
                        Input = caseElement.GetProperty("input").GetString() ?? "",
                        Score = caseElement.TryGetProperty("score", out var score) ? score.GetInt32() : 10
                    };

                    if (caseElement.TryGetProperty("requiredTools", out var tools))
                    {
                        foreach (var tool in tools.EnumerateArray())
                        {
                            benchmarkCase.RequiredTools.Add(tool.GetString() ?? "");
                        }
                    }

                    if (caseElement.TryGetProperty("keywords", out var keywords))
                    {
                        foreach (var kw in keywords.EnumerateArray())
                        {
                            benchmarkCase.Keywords.Add(kw.GetString() ?? "");
                        }
                    }

                    cases.Add(benchmarkCase);
                }
            }

            // Load vision cases
            if (doc.RootElement.TryGetProperty("vision", out var visionCases))
            {
                foreach (var caseElement in visionCases.EnumerateArray())
                {
                    var benchmarkCase = new BenchmarkCase
                    {
                        Id = caseElement.GetProperty("id").GetString() ?? "",
                        Name = caseElement.GetProperty("name").GetString() ?? "",
                        Category = "vision",
                        Input = caseElement.GetProperty("input").GetString() ?? "",
                        Score = caseElement.TryGetProperty("score", out var score) ? score.GetInt32() : 10,
                        ImagePath = caseElement.TryGetProperty("imagePath", out var imgPath)
                            ? imgPath.GetString() : null
                    };

                    if (caseElement.TryGetProperty("expectedContent", out var expected))
                    {
                        foreach (var exp in expected.EnumerateArray())
                        {
                            benchmarkCase.ExpectedContent.Add(exp.GetString() ?? "");
                        }
                    }

                    cases.Add(benchmarkCase);
                }
            }

            _logger.LogInformation("Loaded {Count} test cases from {Path}", cases.Count, casesFile);
            return cases;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load test cases from {Path}, using defaults", casesFile);
            return GetDefaultCases();
        }
    }

    private async Task<CaseResult> EvaluateToolCaseAsync(
        BenchmarkCase testCase,
        CancellationToken cancellationToken)
    {
        var result = new CaseResult
        {
            CaseId = testCase.Id,
            CaseName = testCase.Name,
            Category = "tool"
        };

        try
        {
            // Run using nbot agent -m for real tool testing
            var output = await RunAgentCommandAsync(testCase.Input, cancellationToken);

            result.ResponseContent = output;
            result.RequestMessages = testCase.Input;

            // Validate tool call by checking keywords in output
            result.Passed = ValidateToolOutput(output, testCase.Keywords);
            result.Score = result.Passed ? testCase.Score : 0;

            _logger.LogInformation("Tool case {CaseId} validation: {Passed}", testCase.Id, result.Passed);
            if (!result.Passed && testCase.Keywords.Count > 0)
            {
                _logger.LogDebug("Expected keywords: {Keywords}, Output: {Output}",
                    string.Join(", ", testCase.Keywords),
                    output.Length > 200 ? output[..200] + "..." : output);
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            result.Passed = false;
            result.Score = 0;
            _logger.LogError(ex, "Error evaluating tool case {CaseId}", testCase.Id);
        }

        return result;
    }

    private bool ValidateToolOutput(string output, List<string> keywords)
    {
        if (string.IsNullOrWhiteSpace(output))
            return false;

        // If no keywords defined, just check if there's a meaningful response
        if (keywords.Count == 0)
            return output.Length > 10;

        // Check if any keyword appears in the output (case insensitive)
        var outputLower = output.ToLowerInvariant();
        var matchedCount = 0;

        foreach (var keyword in keywords)
        {
            if (outputLower.Contains(keyword.ToLowerInvariant()))
            {
                matchedCount++;
            }
        }

        // Require at least 50% of keywords to match, or at least 1 if there are many keywords
        var requiredMatchCount = Math.Max(1, keywords.Count / 2);
        return matchedCount >= requiredMatchCount;
    }

    private async Task<CaseResult> EvaluateVisionCaseAsync(
        BenchmarkCase testCase,
        CancellationToken cancellationToken)
    {
        var result = new CaseResult
        {
            CaseId = testCase.Id,
            CaseName = testCase.Name,
            Category = "vision"
        };

        try
        {
            // Get the full path to the test image
            var imagePath = testCase.ImagePath != null
                ? Path.Combine(_benchmarkPath, testCase.ImagePath)
                : null;

            if (imagePath == null || !File.Exists(imagePath))
            {
                result.ErrorMessage = $"Test image not found: {imagePath}";
                result.Passed = false;
                result.Score = 0;
                return result;
            }

            // Build vision prompt with image (English)
            var messages = new List<Microsoft.Extensions.AI.ChatMessage>
            {
                new(Microsoft.Extensions.AI.ChatRole.System, "You are an AI assistant. Please answer questions about images truthfully."),
                new(Microsoft.Extensions.AI.ChatRole.User, $"[Image: {imagePath}]\n{testCase.Input}")
            };

            result.RequestMessages = $"Image: {imagePath}\nQuestion: {testCase.Input}";

            // For now, we'll use direct chat client call - but vision requires proper implementation
            // This is a placeholder - actual vision testing would need the ChatClient to support image input
            // TODO: Implement proper vision testing with IChatClient that supports images

            result.Passed = false; // Not implemented yet - need proper vision support
            result.Score = 0;
            result.ErrorMessage = "Vision test requires IChatClient with vision support - not yet implemented";

            _logger.LogWarning("Vision test not fully implemented for case {CaseId}", testCase.Id);
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            result.Passed = false;
            result.Score = 0;
            _logger.LogError(ex, "Error evaluating vision case {CaseId}", testCase.Id);
        }

        return result;
    }

    private async Task<string> RunAgentCommandAsync(string input, CancellationToken cancellationToken)
    {
        var nbotExe = GetNbotExecutable();
        var args = $"agent -m \"{input}\"";

        // If using dotnet, need to specify project path
        if (nbotExe == "dotnet")
        {
            var projectPath = GetProjectPath();
            if (!string.IsNullOrEmpty(projectPath))
            {
                args = $"run --project \"{projectPath}\" -- {args}";
            }
        }

        _logger.LogInformation("Running: {Exe} {Args}", nbotExe, args);
        _logger.LogInformation("Working directory: {Dir}", GetProjectPath());

        var startInfo = new ProcessStartInfo
        {
            FileName = nbotExe,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = GetProjectPath()
        };

        // Add environment variables - need HOME for config lookup
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["HOME"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        // Set PATH to ensure dotnet can be found
        startInfo.Environment["PATH"] = Environment.GetEnvironmentVariable("PATH");

        using var process = new Process { StartInfo = startInfo };
        var output = new List<string>();
        var error = new List<string>();

        process.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null) output.Add(e.Data);
        };
        process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null) error.Add(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Wait for completion with timeout
        var completed = await Task.Run(() => process.WaitForExit(60000), cancellationToken);

        if (!completed)
        {
            try { process.Kill(); } catch { }
            throw new TimeoutException("Agent command timed out after 60 seconds");
        }

        if (error.Count > 0 && !string.IsNullOrWhiteSpace(string.Join("\n", error)))
        {
            _logger.LogWarning("Agent stderr: {Error}", string.Join("\n", error));
        }

        return string.Join("\n", output);
    }

    private string GetNbotExecutable()
    {
        // Try to find nbot in PATH first
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv != null)
        {
            foreach (var path in pathEnv.Split(Path.PathSeparator))
            {
                var nbotPath = Path.Combine(path, "nbot");
                if (File.Exists(nbotPath))
                    return nbotPath;
            }
        }

        // Return dotnet - we'll specify project path in RunAgentCommandAsync
        return "dotnet";
    }

    private string GetProjectPath()
    {
        // Navigate from benchmark directory to project root
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var projectDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        _logger.LogInformation("Base directory: {Base}", baseDir);
        _logger.LogInformation("Calculated project dir: {Dir}", projectDir);
        var cliProject = Path.Combine(projectDir, "src", "NanoBot.Cli");
        if (Directory.Exists(cliProject))
        {
            _logger.LogInformation("Found CLI project at: {Path}", cliProject);
            return cliProject;
        }
        return "";
    }

    private ModelCapabilities EvaluateCapabilities(BenchmarkResult result)
    {
        return new ModelCapabilities
        {
            SupportsToolCalling = result.ToolPassCount > 0,
            SupportsVision = result.VisionPassCount > 0
        };
    }

    private static List<BenchmarkCase> GetDefaultCases()
    {
        return new List<BenchmarkCase>
        {
            new() { Id = "tool_file_read", Name = "文件读取", Category = "tool",
                Input = "List files in /tmp directory", Score = 10,
                Keywords = new List<string> { "ls", "tmp", "list" } },
            new() { Id = "tool_file_write", Name = "文件写入", Category = "tool",
                Input = "Create a file at /tmp/test.txt with content 'hello'", Score = 10,
                Keywords = new List<string> { "write", "create", "file" } },
            new() { Id = "tool_shell", Name = "Shell命令", Category = "tool",
                Input = "Execute pwd command", Score = 10,
                Keywords = new List<string> { "pwd" } },
            new() { Id = "tool_web_search", Name = "网页搜索", Category = "tool",
                Input = "Search for today's weather", Score = 10,
                Keywords = new List<string> { "search", "weather" } },
            new() { Id = "tool_multi", Name = "多工具调用", Category = "tool",
                Input = "First list /tmp directory, then create a file there", Score = 15,
                Keywords = new List<string> { "list", "write", "tmp" } },
            new() { Id = "browser_navigate", Name = "浏览器导航", Category = "tool",
                Input = "Open https://example.com webpage", Score = 10,
                Keywords = new List<string> { "browser", "example.com", "visit" } },
            new() { Id = "browser_click", Name = "浏览器点击", Category = "tool",
                Input = "Click the search button on the page", Score = 10,
                Keywords = new List<string> { "click", "button", "browser" } },
            new() { Id = "browser_type", Name = "浏览器输入", Category = "tool",
                Input = "Type 'hello world' in the search box", Score = 10,
                Keywords = new List<string> { "type", "input", "hello" } },
        };
    }
}