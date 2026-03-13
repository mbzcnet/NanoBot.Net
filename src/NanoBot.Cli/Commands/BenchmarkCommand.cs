using System.CommandLine;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using NanoBot.Core.Benchmark;
using NanoBot.Core.Configuration;
using NanoBot.Providers;

namespace NanoBot.Cli.Commands;

public class BenchmarkCommand : NanoBotCommandBase
{
    public override string Name => "benchmark";
    public override string Description => "模型可用性评测工具";

    public override Command CreateCommand()
    {
        var profileOption = new Option<string>(
            name: "--profile",
            description: "LLM 配置 profile 名称",
            getDefaultValue: () => "default");

        var command = new Command(Name, Description)
        {
            profileOption
        };

        command.SetHandler(async (context) =>
        {
            var profile = context.ParseResult.GetValueForOption(profileOption);
            var cancellationToken = context.GetCancellationToken();
            await ExecuteBenchmarkAsync(profile, cancellationToken);
        });

        return command;
    }

    private async Task ExecuteBenchmarkAsync(
        string? profileName,
        CancellationToken cancellationToken)
    {
        var config = GetConfig();
        var configPath = GetConfigPath();

        profileName ??= config.Llm.DefaultProfile ?? "default";

        Console.WriteLine("🔧 模型可用性评测工具\n");

        if (!config.Llm.Profiles.TryGetValue(profileName, out var profile))
        {
            Console.WriteLine($"❌ 错误: Profile '{profileName}' 不存在");
            Console.WriteLine($"可用的 profiles: {string.Join(", ", config.Llm.Profiles.Keys)}");
            return;
        }

        Console.WriteLine($"评测 profile: {profileName}");
        Console.WriteLine($"模型: {profile.Provider}/{profile.Model}\n");

        try
        {
            // Create ChatClient
            var chatClientFactory = GetService<IChatClientFactory>();
            var chatClient = chatClientFactory.CreateChatClient(
                profile.Provider ?? "openai",
                profile.Model,
                profile.ApiKey,
                profile.ApiBase);

            // Run benchmark
            var benchmarkEngine = GetService<IBenchmarkEngine>();
            var emptyTools = new List<object>();
            var result = await benchmarkEngine.RunBenchmarkAsync(
                chatClient,
                emptyTools,
                cancellationToken);

            // Update result with actual model info
            result.Model = profile.Model;
            result.Provider = profile.Provider ?? "unknown";

            // Output results
            OutputResults(result);

            // Save to config
            await SaveCapabilitiesAsync(configPath, profileName, result, cancellationToken);

            // Save detailed results to workspace/.benchmark
            var workspacePath = config.Workspace?.Path?.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nbot", "workspace");
            await SaveDetailedResultsAsync(result, workspacePath, cancellationToken);

            Console.WriteLine($"\n✅ 评测完成");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ 评测失败: {ex.Message}");
        }
    }

    private static void OutputResults(BenchmarkResult result)
    {
        Console.WriteLine("------------------------------------------------------------");
        Console.WriteLine("                    评测结果");
        Console.WriteLine("------------------------------------------------------------\n");

        var status = result.Passed ? "✅ 通过" : "❌ 未通过";
        Console.WriteLine($"  总分: {result.FinalScore} / 100 ({result.FinalScore}分)");
        Console.WriteLine($"    - 工具调用: {result.ToolScore}/80 ({result.ToolPassCount}/{result.ToolTotalCount} 通过)");
        Console.WriteLine($"    - 图像处理: {result.VisionScore}/20 ({result.VisionPassCount}/{result.VisionTotalCount} 通过)");
        Console.WriteLine($"  状态: {status}\n");

        Console.WriteLine("  详细结果:");
        foreach (var caseResult in result.CaseResults)
        {
            var passMark = caseResult.Passed ? "✅" : "❌";
            Console.WriteLine($"    {passMark} {caseResult.CaseId,-20} {caseResult.CaseName,-15} [{caseResult.Category}]");
        }

        Console.WriteLine("\n------------------------------------------------------------");
        Console.WriteLine("                    能力评估");
        Console.WriteLine("------------------------------------------------------------\n");

        var toolStatus = result.Capabilities.SupportsToolCalling ? "✅ 支持" : "❌ 不支持";
        var visionStatus = result.Capabilities.SupportsVision ? "✅ 支持" : "❌ 不支持";

        Console.WriteLine($"  工具调用: {toolStatus}");
        Console.WriteLine($"  图像处理: {visionStatus}");
        Console.WriteLine($"  评分: {result.FinalScore}/100");
    }

    private static async Task SaveCapabilitiesAsync(
        string configPath,
        string profileName,
        BenchmarkResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            // Read and parse the original JSON using JsonNode (mutable)
            var json = await File.ReadAllTextAsync(configPath, cancellationToken);
            var node = JsonNode.Parse(json);
            if (node == null)
            {
                Console.WriteLine($"\n⚠️ 配置文件解析失败");
                return;
            }

            // Navigate to llm.profiles.{profileName}
            var llm = node?["llm"];
            var profiles = llm?["profiles"];
            var profile = profiles?[profileName];

            if (profile == null)
            {
                Console.WriteLine($"\n⚠️ 未找到 profile '{profileName}'");
                return;
            }

            // Update or add capabilities
            var capabilities = new JsonObject
            {
                ["supportsVision"] = result.Capabilities.SupportsVision,
                ["supportsToolCalling"] = result.Capabilities.SupportsToolCalling,
                ["lastBenchmarkTime"] = result.Timestamp.ToString("o"),
                ["score"] = result.FinalScore,
                ["toolScore"] = result.ToolScore,
                ["visionScore"] = result.VisionScore
            };
            profile["capabilities"] = capabilities;

            // Serialize and save
            var options = new JsonSerializerOptions { WriteIndented = true };
            var resultJson = JsonSerializer.Serialize(node, options);
            await File.WriteAllTextAsync(configPath, resultJson, cancellationToken);

            Console.WriteLine($"\n📝 结果已保存到配置文件: {configPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n⚠️ 保存结果失败: {ex.Message}");
        }
    }

    private static async Task SaveDetailedResultsAsync(
        BenchmarkResult result,
        string workspacePath,
        CancellationToken cancellationToken)
    {
        try
        {
            // Create benchmark directory with timestamp
            var timestamp = result.Timestamp.ToString("yyyy-MM-dd_HH-mm-ss");
            var benchmarkDir = Path.Combine(workspacePath, ".benchmark", timestamp);
            Directory.CreateDirectory(benchmarkDir);

            // Save summary.json
            var summary = new
            {
                result.Timestamp,
                result.Model,
                result.Provider,
                result.FinalScore,
                result.ToolScore,
                result.VisionScore,
                result.ToolPassCount,
                result.ToolTotalCount,
                result.VisionPassCount,
                result.VisionTotalCount,
                result.Passed,
                result.Capabilities
            };
            var summaryJson = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(benchmarkDir, "summary.json"), summaryJson, cancellationToken);

            // Save each case result
            foreach (var caseResult in result.CaseResults)
            {
                var caseDir = Path.Combine(benchmarkDir, caseResult.CaseId);
                Directory.CreateDirectory(caseDir);

                // Save input.json
                var input = new
                {
                    caseResult.CaseId,
                    caseResult.CaseName,
                    caseResult.Category,
                    caseResult.Score
                };
                await File.WriteAllTextAsync(
                    Path.Combine(caseDir, "input.json"),
                    JsonSerializer.Serialize(input, new JsonSerializerOptions { WriteIndented = true }),
                    cancellationToken);

                // Save output.json
                var output = new
                {
                    caseResult.Passed,
                    caseResult.ErrorMessage,
                    caseResult.ResponseContent
                };
                await File.WriteAllTextAsync(
                    Path.Combine(caseDir, "output.json"),
                    JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }),
                    cancellationToken);
            }

            Console.WriteLine($"\n📁 详细结果已保存到: {benchmarkDir}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n⚠️ 保存详细结果失败: {ex.Message}");
        }
    }
}