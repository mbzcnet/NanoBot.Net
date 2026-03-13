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
        var profileOption = new Option<string?>(
            name: "--profile",
            description: "LLM 配置 profile 名称（默认测试 default，使用 --all 测试所有）");

        var allOption = new Option<bool>(
            name: "--all",
            description: "测试所有 LLM 配置",
            getDefaultValue: () => false);

        var forceOption = new Option<bool>(
            name: "--force",
            description: "强制覆盖已有测试结果，不询问",
            getDefaultValue: () => false);

        var command = new Command(Name, Description)
        {
            profileOption,
            allOption,
            forceOption
        };

        command.SetHandler(async (context) =>
        {
            var profile = context.ParseResult.GetValueForOption(profileOption);
            var all = context.ParseResult.GetValueForOption(allOption);
            var force = context.ParseResult.GetValueForOption(forceOption);
            var cancellationToken = context.GetCancellationToken();
            await ExecuteBenchmarkAsync(profile, all, force, cancellationToken);
        });

        return command;
    }

    private async Task ExecuteBenchmarkAsync(
        string? profileName,
        bool testAll,
        bool force,
        CancellationToken cancellationToken)
    {
        var config = GetConfig();
        var configPath = GetConfigPath();

        // 确定要测试的 profiles
        List<string> profilesToTest;
        if (testAll)
        {
            profilesToTest = config.Llm.Profiles.Keys.ToList();
            Console.WriteLine($"🔧 模型可用性评测工具 - 测试所有配置 ({profilesToTest.Count} 个)\n");
        }
        else
        {
            profileName ??= config.Llm.DefaultProfile ?? "default";
            if (!config.Llm.Profiles.ContainsKey(profileName))
            {
                Console.WriteLine($"❌ 错误: Profile '{profileName}' 不存在");
                Console.WriteLine($"可用的 profiles: {string.Join(", ", config.Llm.Profiles.Keys)}");
                return;
            }
            profilesToTest = [profileName];
            Console.WriteLine($"🔧 模型可用性评测工具\n");
        }

        // 检查是否已有测试结果
        if (!force && !testAll)
        {
            var targetProfile = profilesToTest[0];
            if (config.Llm.Profiles.TryGetValue(targetProfile, out var profile) && 
                profile.Capabilities?.LastBenchmarkTime != null)
            {
                Console.WriteLine($"⚠️ Profile '{targetProfile}' 已有测试结果:");
                Console.WriteLine($"   评分: {profile.Capabilities.Score}/100");
                Console.WriteLine($"   测试时间: {profile.Capabilities.LastBenchmarkTime:yyyy-MM-dd HH:mm:ss}");
                Console.Write($"\n是否重新测试并覆盖? [y/N]: ");
                
                var response = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (response != "y" && response != "yes")
                {
                    Console.WriteLine("已取消测试。");
                    return;
                }
                Console.WriteLine();
            }
        }

        var results = new List<(string ProfileName, BenchmarkResult Result)>();
        var failedProfiles = new List<string>();

        // 测试每个 profile
        foreach (var profileToTest in profilesToTest)
        {
            if (!config.Llm.Profiles.TryGetValue(profileToTest, out var profile))
            {
                Console.WriteLine($"❌ Profile '{profileToTest}' 不存在，跳过");
                failedProfiles.Add(profileToTest);
                continue;
            }

            Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            Console.WriteLine($"测试 Profile: {profileToTest}");
            Console.WriteLine($"模型: {profile.Provider}/{profile.Model}");
            
            // 检查是否已有测试结果（批量测试时）
            if (!force && testAll && profile.Capabilities?.LastBenchmarkTime != null)
            {
                Console.WriteLine($"   已有测试结果: {profile.Capabilities.Score}/100");
                Console.Write($"   是否重新测试? [y/N]: ");
                
                var response = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (response != "y" && response != "yes")
                {
                    Console.WriteLine($"   跳过 '{profileToTest}'\n");
                    continue;
                }
            }
            Console.WriteLine();

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
                var emptyTools = new List<AITool>();
                var result = await benchmarkEngine.RunBenchmarkAsync(
                    chatClient,
                    emptyTools,
                    cancellationToken);

                // Update result with actual model info
                result.Model = profile.Model;
                result.Provider = profile.Provider ?? "unknown";

                // Output results
                OutputResults(result);

                results.Add((profileToTest, result));

                // Save detailed results immediately for each profile
                var workspacePath = config.Workspace?.Path?.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
                    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nbot", "workspace");
                await SaveDetailedResultsAsync(result, workspacePath, configPath, profileToTest, cancellationToken);

                Console.WriteLine($"✅ Profile '{profileToTest}' 测试完成\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ Profile '{profileToTest}' 测试失败: {ex.Message}");
                failedProfiles.Add(profileToTest);
            }
        }

        // 保存所有结果到配置文件（一次性保存，避免多次读写）
        if (results.Count > 0)
        {
            await SaveAllCapabilitiesAsync(configPath, results, cancellationToken);
        }

        // 输出总结
        Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine($"评测总结");
        Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine($"测试完成: {results.Count}/{profilesToTest.Count}");
        
        if (results.Count > 0)
        {
            Console.WriteLine($"\n详细结果:");
            foreach (var (name, result) in results)
            {
                var status = result.Passed ? "✅" : "❌";
                Console.WriteLine($"  {status} {name,-15} {result.FinalScore}/100");
            }
        }

        if (failedProfiles.Count > 0)
        {
            Console.WriteLine($"\n测试失败: {string.Join(", ", failedProfiles)}");
        }

        Console.WriteLine($"\n✅ 评测完成");
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

    private static async Task SaveAllCapabilitiesAsync(
        string configPath,
        List<(string ProfileName, BenchmarkResult Result)> results,
        CancellationToken cancellationToken)
    {
        try
        {
            // 读取原始文件内容
            var json = await File.ReadAllTextAsync(configPath, cancellationToken);
            var node = JsonNode.Parse(json);
            if (node == null)
            {
                Console.WriteLine($"\n⚠️ 配置文件解析失败");
                return;
            }

            // 确保 llm.profiles 存在
            var llm = node["llm"] ??= new JsonObject();
            var profiles = llm["profiles"] ??= new JsonObject();

            // 更新每个 profile 的 capabilities
            foreach (var (profileName, result) in results)
            {
                var profile = profiles[profileName];
                if (profile == null)
                {
                    Console.WriteLine($"\n⚠️ 未找到 profile '{profileName}'");
                    continue;
                }

                // 更新或添加 capabilities
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
            }

            // 序列化并保存（保持原有格式）
            var options = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            };
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
        string configPath,
        string profileName,
        CancellationToken cancellationToken)
    {
        try
        {
            // 创建目录结构: .benchmark/{profileName}/{timestamp}/
            var timestamp = result.Timestamp.ToString("yyyy-MM-dd_HH-mm-ss");
            var benchmarkDir = Path.Combine(workspacePath, ".benchmark", profileName, timestamp);
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
                result.Capabilities,
                ConfigPath = configPath,
                ProfileName = profileName
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
                    caseResult.Score,
                    caseResult.RequestMessages
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

            Console.WriteLine($"📁 详细结果已保存到: {benchmarkDir}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ 保存详细结果失败: {ex.Message}");
        }
    }
}