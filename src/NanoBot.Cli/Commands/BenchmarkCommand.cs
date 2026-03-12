using System.CommandLine;
using System.Text.Json;
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
        Console.WriteLine($"  总分: {result.TotalScore} / {result.MaxScore} ({result.ScorePercentage:F1}%)");
        Console.WriteLine($"  状态: {status}\n");

        Console.WriteLine("  详细结果:");
        foreach (var caseResult in result.CaseResults)
        {
            var passMark = caseResult.Passed ? "✅" : "❌";
            Console.WriteLine($"    {passMark} {caseResult.CaseId,-20} {caseResult.CaseName,-15} {caseResult.Score}/{caseResult.Score}");
        }

        Console.WriteLine("\n------------------------------------------------------------");
        Console.WriteLine("                    能力评估");
        Console.WriteLine("------------------------------------------------------------\n");

        var toolStatus = result.Capabilities.SupportsToolCalling ? "✅ 支持" : "❌ 不支持";
        var visionStatus = result.Capabilities.SupportsVision ? "✅ 支持" : "❌ 不支持";

        Console.WriteLine($"  工具调用: {toolStatus}");
        Console.WriteLine($"  图像处理: {visionStatus}");
    }

    private static async Task SaveCapabilitiesAsync(
        string configPath,
        string profileName,
        BenchmarkResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(configPath, cancellationToken);
            var config = JsonSerializer.Deserialize<AgentConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (config?.Llm?.Profiles?.TryGetValue(profileName, out var profile) == true)
            {
                profile.Capabilities = new Core.Benchmark.ModelCapabilities
                {
                    SupportsToolCalling = result.Capabilities.SupportsToolCalling,
                    SupportsVision = result.Capabilities.SupportsVision,
                    LastBenchmarkTime = result.Timestamp
                };

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var updatedJson = JsonSerializer.Serialize(config, options);
                await File.WriteAllTextAsync(configPath, updatedJson, cancellationToken);

                Console.WriteLine($"\n📝 结果已保存到配置文件: {configPath}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n⚠️ 保存结果失败: {ex.Message}");
        }
    }
}