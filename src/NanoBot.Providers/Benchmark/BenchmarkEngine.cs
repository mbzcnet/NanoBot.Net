using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
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
        _benchmarkPath = ResolveBenchmarkPath();
        _logger.LogInformation("Benchmark path resolved to: {Path}", _benchmarkPath);
    }

    private static string ResolveBenchmarkPath()
    {
        // Try multiple possible locations for the benchmark directory
        var possiblePaths = new[]
        {
            // Try relative to current domain base (development scenario)
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "src", "benchmark"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "benchmark"),
            // Try relative to current domain base (published scenario)
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "benchmark"),
            // Try relative to current working directory
            Path.Combine(Directory.GetCurrentDirectory(), "src", "benchmark"),
            Path.Combine(Directory.GetCurrentDirectory(), "benchmark"),
        };

        foreach (var path in possiblePaths)
        {
            var fullPath = Path.GetFullPath(path);
            if (Directory.Exists(fullPath) && File.Exists(Path.Combine(fullPath, "cases.json")))
            {
                return fullPath;
            }
        }

        // Fallback to the most likely path
        return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "src", "benchmark"));
    }

    public async Task<BenchmarkResult> RunBenchmarkAsync(
        IChatClient chatClient,
        IReadOnlyList<AITool> tools,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine("[DEBUG] BenchmarkEngine.RunBenchmarkAsync started");
        
        var result = new BenchmarkResult
        {
            Timestamp = DateTime.UtcNow
        };

        var cases = LoadTestCases();
        Console.WriteLine($"[DEBUG] Loaded {cases.Count} test cases");
        
        var toolCases = cases.Where(c => c.Category == "tool").ToList();
        var visionCases = cases.Where(c => c.Category == "vision").ToList();

        Console.WriteLine($"[DEBUG] Tool cases: {toolCases.Count}, Vision cases: {visionCases.Count}");
        Console.WriteLine($"[DEBUG] Benchmark path: {_benchmarkPath}");
        Console.WriteLine($"[DEBUG] Workspace path: {_workspacePath}");

        // Run tool tests

        // Run tool tests
        foreach (var testCase in toolCases)
        {
            Console.WriteLine($"[DEBUG] Starting tool case: {testCase.Id}");
            var caseResult = await EvaluateToolCaseAsync(testCase, cancellationToken);
            Console.WriteLine($"[DEBUG] Completed tool case: {testCase.Id}, Passed: {caseResult.Passed}");
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
        _logger.LogInformation("Starting {Count} vision test cases...", visionCases.Count);
        foreach (var testCase in visionCases)
        {
            _logger.LogInformation("Running vision case: {CaseId} - {CaseName}", testCase.Id, testCase.Name);
            var caseResult = await EvaluateVisionCaseAsync(testCase, chatClient, cancellationToken);
            caseResult.Category = "vision";
            result.CaseResults.Add(caseResult);

            if (caseResult.Passed)
            {
                result.TotalScore += caseResult.Score;
                result.VisionPassCount++;
            }
            result.VisionTotalCount++;

            _logger.LogInformation("Vision case {CaseId}: {Passed} (Score: {Score})", caseResult.CaseId, caseResult.Passed, caseResult.Score);
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
            Category = "tool",
            RequestMessages = testCase.Input  // Set early to ensure it's captured even if execution fails
        };

        try
        {
            // Build prompt with explicit tool calling instruction
            var prompt = BuildToolPrompt(testCase);

            // Run using nbot agent -m for real tool testing
            var output = await RunAgentCommandAsync(prompt, cancellationToken);

            result.ResponseContent = output;

            // Validate tool call by checking tool calls and keywords
            result.Passed = ValidateToolOutput(output, testCase.Keywords, testCase.RequiredTools);
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

        // Save intermediate result immediately
        await SaveIntermediateResultAsync(result, testCase.Input);

        return result;
    }

    private async Task SaveIntermediateResultAsync(CaseResult result, string input)
    {
        try
        {
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
            var caseDir = Path.Combine(_workspacePath, ".benchmark", "ollama_qwen3.5", timestamp, result.CaseId);
            Directory.CreateDirectory(caseDir);

            // Save input
            await File.WriteAllTextAsync(
                Path.Combine(caseDir, "input.json"),
                JsonSerializer.Serialize(new { result.CaseId, result.CaseName, Input = input }, new JsonSerializerOptions { WriteIndented = true }));

            // Save output
            await File.WriteAllTextAsync(
                Path.Combine(caseDir, "output.json"),
                JsonSerializer.Serialize(new { result.Passed, result.Score, result.ErrorMessage, ResponseContent = result.ResponseContent }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to save intermediate result: {Error}", ex.Message);
        }
    }

    private string BuildToolPrompt(BenchmarkCase testCase)
    {
        // Add explicit instructions to encourage tool calling
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("You have access to tools that can help complete tasks.");

        if (testCase.RequiredTools.Count > 0)
        {
            sb.AppendLine($"For this task, you should use the following tool(s): {string.Join(", ", testCase.RequiredTools)}");
        }

        sb.AppendLine("Call the appropriate tool with the correct parameters to complete the task.");
        sb.AppendLine("Format your tool call using [TOOL_CALL]tool_name(parameters)[/TOOL_CALL] syntax.");
        sb.AppendLine();
        sb.AppendLine($"Task: {testCase.Input}");

        return sb.ToString();
    }

    private bool ValidateToolOutput(string output, List<string> keywords, List<string> requiredTools)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            _logger.LogWarning("Output is empty");
            return false;
        }

        // Check for tool call markers [TOOL_CALL]...[/TOOL_CALL]
        var toolCallPattern = @"\[TOOL_CALL\](.*?)\[/TOOL_CALL\]";
        var matches = System.Text.RegularExpressions.Regex.Matches(output, toolCallPattern);

        // Check for <function_call>...</function_call> format (XML style)
        var xmlFunctionPattern = @"<function_call[^>]*>(.*?)</function_call>";
        var xmlMatches = System.Text.RegularExpressions.Regex.Matches(output, xmlFunctionPattern, System.Text.RegularExpressions.RegexOptions.Singleline);

        // Also check for function call patterns like exec(command="...") or tool(...)
        var functionCallPattern = @"(exec|read_file|write_file|list_dir|edit|web_search|web_fetch|browser|browser_navigate|browser_click|browser_type|browser_scroll|browser_snapshot)\s*\(";
        var functionMatches = System.Text.RegularExpressions.Regex.Matches(output, functionCallPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Check for function call intent markers like "name="tool_name"" or "name='tool_name'"
        var nameAttributePattern = "name\\s*=\\s*[\"'](exec|read_file|write_file|list_dir|edit|web_search|web_fetch|browser|browser_navigate|browser_click|browser_type|browser_scroll|browser_snapshot)[\"']";
        var nameMatches = System.Text.RegularExpressions.Regex.Matches(output, nameAttributePattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        var totalMatches = matches.Count + xmlMatches.Count + functionMatches.Count + nameMatches.Count;

        if (totalMatches == 0)
        {
            _logger.LogWarning("No tool calls found in output");
            return false;
        }

        _logger.LogInformation("Found {Count} tool calls in output (from [TOOL_CALL]: {DirectCount}, from XML: {XmlCount}, from pattern: {PatternCount}, from name attr: {NameCount})",
            totalMatches, matches.Count, xmlMatches.Count, functionMatches.Count, nameMatches.Count);

        // Extract tool names from tool calls
        var calledTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // From [TOOL_CALL] format
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var toolCall = match.Groups[1].Value;
            var toolName = toolCall.Split('(')[0].Trim();
            if (!string.IsNullOrWhiteSpace(toolName))
            {
                calledTools.Add(toolName);
                _logger.LogDebug("Detected tool call: {ToolName}", toolName);
            }
        }

        // From XML function_call format
        foreach (System.Text.RegularExpressions.Match match in xmlMatches)
        {
            var xmlContent = match.Groups[1].Value;
            // Try to extract name attribute
            var namePattern = "name\\s*=\\s*[\"']([^\"']+)[\"']";
            var nameMatch = System.Text.RegularExpressions.Regex.Match(xmlContent, namePattern);
            if (nameMatch.Success)
            {
                var toolName = nameMatch.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(toolName))
                {
                    calledTools.Add(toolName);
                    _logger.LogDebug("Detected tool call from XML: {ToolName}", toolName);
                }
            }
        }

        // From function call pattern
        foreach (System.Text.RegularExpressions.Match match in functionMatches)
        {
            var toolName = match.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(toolName))
            {
                calledTools.Add(toolName);
                _logger.LogDebug("Detected tool call from pattern: {ToolName}", toolName);
            }
        }

        // From name attribute pattern
        foreach (System.Text.RegularExpressions.Match match in nameMatches)
        {
            var toolName = match.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(toolName))
            {
                calledTools.Add(toolName);
                _logger.LogDebug("Detected tool call from name attribute: {ToolName}", toolName);
            }
        }

        // Check if required tools are called
        if (requiredTools.Count > 0)
        {
            var requiredMatchCount = 0;
            foreach (var requiredTool in requiredTools)
            {
                // Check for exact match or partial match (e.g., "browser" matches "browser_navigate")
                if (calledTools.Any(t => t.Equals(requiredTool, StringComparison.OrdinalIgnoreCase) ||
                                         t.Contains(requiredTool, StringComparison.OrdinalIgnoreCase) ||
                                         requiredTool.Contains(t, StringComparison.OrdinalIgnoreCase)))
                {
                    requiredMatchCount++;
                    _logger.LogDebug("Required tool matched: {RequiredTool}", requiredTool);
                }
            }

            // For multi-tool tests, require at least 1 tool to match
            // For single-tool tests, require the specific tool
            var requiredThreshold = requiredTools.Count > 1 ? 1 : 1;
            if (requiredMatchCount >= requiredThreshold)
            {
                _logger.LogInformation("Required tools matched: {Matched}/{Total} (threshold: {Threshold})", 
                    requiredMatchCount, requiredTools.Count, requiredThreshold);
                return true;
            }
            
            _logger.LogWarning("Required tools not matched: {Matched}/{Total}", requiredMatchCount, requiredTools.Count);
        }

        // Fallback to keyword matching if no required tools specified or not matched
        if (keywords.Count > 0)
        {
            var outputLower = output.ToLowerInvariant();
            var matchedCount = 0;

            foreach (var keyword in keywords)
            {
                if (outputLower.Contains(keyword.ToLowerInvariant()))
                {
                    matchedCount++;
                }
            }

            var requiredMatchCount = Math.Max(1, keywords.Count / 2);
            var keywordMatched = matchedCount >= requiredMatchCount;
            _logger.LogInformation("Keyword matching: {Matched}/{Required} - {Result}", 
                matchedCount, requiredMatchCount, keywordMatched ? "PASSED" : "FAILED");
            return keywordMatched;
        }

        // If we have tool calls but no specific requirements, consider it a pass
        return matches.Count > 0;
    }

    private async Task<CaseResult> EvaluateVisionCaseAsync(
        BenchmarkCase testCase,
        IChatClient chatClient,
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

            // Load image data
            var imageBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
            var mediaType = GetImageMediaType(imagePath);

            // Build vision prompt with image
            var contents = new List<AIContent>
            {
                new DataContent(imageBytes, mediaType),
                new TextContent(testCase.Input)
            };

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, "You are an AI assistant. Please answer questions about images truthfully."),
                new(ChatRole.User, contents)
            };

            result.RequestMessages = $"Image: {imagePath}\nQuestion: {testCase.Input}";

            _logger.LogInformation("Sending vision request for case {CaseId} with image {ImagePath}", testCase.Id, imagePath);

            // Send request to chat client
            var response = await chatClient.GetResponseAsync(
                messages,
                cancellationToken: cancellationToken);

            var responseText = response.Text ?? "";
            result.ResponseContent = responseText;

            _logger.LogInformation("Vision case {CaseId} response: {Response}", testCase.Id, responseText[..Math.Min(200, responseText.Length)]);

            // Validate response by checking expected content
            result.Passed = ValidateVisionOutput(responseText, testCase.ExpectedContent);
            result.Score = result.Passed ? testCase.Score : 0;

            _logger.LogInformation("Vision case {CaseId} validation: {Passed}", testCase.Id, result.Passed);
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

    private string GetImageMediaType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            _ => "image/png"
        };
    }

    private bool ValidateVisionOutput(string output, List<string> expectedContent)
    {
        if (string.IsNullOrWhiteSpace(output))
            return false;

        // If no expected content defined, just check if there's a meaningful response
        if (expectedContent.Count == 0)
            return output.Length > 10;

        // Check if any expected content appears in the output (case insensitive)
        var outputLower = output.ToLowerInvariant();
        var matchedCount = 0;

        foreach (var content in expectedContent)
        {
            if (outputLower.Contains(content.ToLowerInvariant()))
            {
                matchedCount++;
            }
        }

        // Require at least 50% of expected content to match, or at least 1 if there are many items
        var requiredMatchCount = Math.Max(1, expectedContent.Count / 2);
        return matchedCount >= requiredMatchCount;
    }

    private async Task<string> RunAgentCommandAsync(string input, CancellationToken cancellationToken)
    {
        var (nbotExe, args) = GetNbotCommand(input);

        _logger.LogInformation("Running: {Exe} {Args}", nbotExe, args);

        var startInfo = new ProcessStartInfo
        {
            FileName = nbotExe,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = GetProjectPath() ?? Directory.GetCurrentDirectory()
        };

        // Add environment variables - need HOME for config lookup
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["HOME"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        startInfo.Environment["PATH"] = Environment.GetEnvironmentVariable("PATH") ?? "";

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

        // Wait for completion with timeout (120 seconds per test case)
        var completed = await Task.Run(() => process.WaitForExit(120000), cancellationToken);

        if (!completed)
        {
            try { process.Kill(); } catch { }
            throw new TimeoutException("Agent command timed out after 120 seconds");
        }

        // Wait a bit for remaining output to be processed
        await Task.Delay(500, cancellationToken);

        if (error.Count > 0 && !string.IsNullOrWhiteSpace(string.Join("\n", error)))
        {
            _logger.LogWarning("Agent stderr: {Error}", string.Join("\n", error));
        }

        // Combine stdout and stderr
        var stdout = string.Join("\n", output);
        var stderr = string.Join("\n", error);
        
        var result = stdout;
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            result += "\n\n[STDERR]\n" + stderr;
        }
        
        _logger.LogInformation("Command output length: {Length} chars (stdout: {Stdout}, stderr: {Stderr})", 
            result.Length, stdout.Length, stderr.Length);
        
        return result;
    }

    private (string exe, string args) GetNbotCommand(string input)
    {
        // Try to find nbot in PATH first
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv != null)
        {
            foreach (var path in pathEnv.Split(Path.PathSeparator))
            {
                var nbotPath = Path.Combine(path, "nbot");
                if (File.Exists(nbotPath))
                    return (nbotPath, $"agent -m \"{input}\" --debug");
            }
        }

        // Try to find compiled DLL
        var dllPath = FindCompiledDll();
        if (!string.IsNullOrEmpty(dllPath))
        {
            return ("dotnet", $"\"{dllPath}\" agent -m \"{input}\" --debug");
        }

        // Fallback to dotnet run (slow, for development only)
        var projectPath = GetProjectPath();
        if (!string.IsNullOrEmpty(projectPath))
        {
            _logger.LogWarning("Using 'dotnet run' for benchmark - this is slow. Consider building the project first.");
            return ("dotnet", $"run --project \"{projectPath}\" -- agent -m \"{input}\" --debug");
        }

        throw new InvalidOperationException("Could not find nbot executable or project");
    }

    private string? FindCompiledDll()
    {
        // Navigate from benchmark directory to find compiled DLL
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var possiblePaths = new[]
        {
            // Try net10.0 first (current target framework)
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "src", "NanoBot.Cli", "bin", "Debug", "net10.0", "NanoBot.Cli.dll"),
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "src", "NanoBot.Cli", "bin", "Release", "net10.0", "NanoBot.Cli.dll"),
            // Fallback to net9.0
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "src", "NanoBot.Cli", "bin", "Debug", "net9.0", "NanoBot.Cli.dll"),
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "src", "NanoBot.Cli", "bin", "Release", "net9.0", "NanoBot.Cli.dll"),
            Path.Combine(baseDir, "NanoBot.Cli.dll"),
        };

        foreach (var path in possiblePaths)
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                _logger.LogInformation("Found compiled DLL: {Path}", fullPath);
                return fullPath;
            }
        }

        return null;
    }

    private string? GetProjectPath()
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