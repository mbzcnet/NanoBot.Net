using System.CommandLine;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using NanoBot.Core.Benchmark;
using NanoBot.Core.Configuration;
using NanoBot.Providers;
using NanoBot.Tools;

namespace NanoBot.Cli.Commands;

public class BenchmarkCommand : NanoBotCommandBase
{
    public override string Name => "benchmark";
    public override string Description => "Model availability benchmarking tool";

    public override Command CreateCommand()
    {
        var profileOption = new Option<string?>(
            name: "--profile",
            description: "LLM profile name (default tests 'default', use --all to test all)");

        var allOption = new Option<bool>(
            name: "--all",
            description: "Test all LLM configurations",
            getDefaultValue: () => false);

        var forceOption = new Option<bool>(
            name: "--force",
            description: "Force overwrite existing test results without prompting",
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

        // Determine which profiles to test
        List<string> profilesToTest;
        if (testAll)
        {
            profilesToTest = config.Llm.Profiles.Keys.ToList();
            Console.WriteLine($"🔧 Model Availability Benchmarking - Testing all configurations ({profilesToTest.Count} profiles)\n");
        }
        else
        {
            profileName ??= config.Llm.DefaultProfile ?? "default";
            if (!config.Llm.Profiles.ContainsKey(profileName))
            {
                Console.WriteLine($"❌ Error: Profile '{profileName}' does not exist");
                Console.WriteLine($"Available profiles: {string.Join(", ", config.Llm.Profiles.Keys)}");
                return;
            }
            profilesToTest = [profileName];
            Console.WriteLine($"🔧 Model Availability Benchmarking\n");
        }

        // Check for existing test results (single profile mode)
        if (!force && !testAll)
        {
            var targetProfile = profilesToTest[0];
            if (config.Llm.Profiles.TryGetValue(targetProfile, out var profile) && 
                profile.Capabilities?.LastBenchmarkTime != null)
            {
                Console.WriteLine($"⚠️ Profile '{targetProfile}' already has test results:");
                Console.WriteLine($"   Score: {profile.Capabilities.Score}/100");
                Console.WriteLine($"   Test time: {profile.Capabilities.LastBenchmarkTime:yyyy-MM-dd HH:mm:ss}");
                Console.Write($"\nRe-test and overwrite? [y/N]: ");
                
                var response = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (response != "y" && response != "yes")
                {
                    Console.WriteLine("Test cancelled.");
                    return;
                }
                Console.WriteLine();
            }
        }

        var results = new List<(string ProfileName, BenchmarkResult Result)>();
        var failedProfiles = new List<string>();

        // Test each profile
        foreach (var profileToTest in profilesToTest)
        {
            if (!config.Llm.Profiles.TryGetValue(profileToTest, out var profile))
            {
                Console.WriteLine($"❌ Profile '{profileToTest}' does not exist, skipping");
                failedProfiles.Add(profileToTest);
                continue;
            }

            Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            Console.WriteLine($"Testing Profile: {profileToTest}");
            Console.WriteLine($"Model: {profile.Provider}/{profile.Model}");

            // Check for existing test results (batch mode)
            if (!force && testAll && profile.Capabilities?.LastBenchmarkTime != null)
            {
                Console.WriteLine($"   Existing result: {profile.Capabilities.Score}/100");
                Console.Write($"   Re-test? [y/N]: ");
                
                var response = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (response != "y" && response != "yes")
                {
                    Console.WriteLine($"   Skipping '{profileToTest}'\n");
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
                var tools = await ToolProvider.CreateDefaultToolsAsync(SharedServiceProvider!, cancellationToken: cancellationToken);
                var result = await benchmarkEngine.RunBenchmarkAsync(
                    chatClient,
                    tools,
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

                Console.WriteLine($"✅ Profile '{profileToTest}' test completed\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ Profile '{profileToTest}' test failed: {ex.Message}");
                failedProfiles.Add(profileToTest);
            }
        }

        // Save all results to config file (single save to avoid multiple reads/writes)
        if (results.Count > 0)
        {
            await SaveAllCapabilitiesAsync(configPath, results, cancellationToken);
        }

        // Output summary
        Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine($"Benchmark Summary");
        Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine($"Completed: {results.Count}/{profilesToTest.Count}");
        
        if (results.Count > 0)
        {
            Console.WriteLine($"\nDetailed results:");
            foreach (var (name, result) in results)
            {
                var status = result.Passed ? "✅" : "❌";
                Console.WriteLine($"  {status} {name,-15} {result.FinalScore}/100");
            }
        }

        if (failedProfiles.Count > 0)
        {
            Console.WriteLine($"\nFailed: {string.Join(", ", failedProfiles)}");
        }

        Console.WriteLine($"\n✅ Benchmark completed");
    }

    private static void OutputResults(BenchmarkResult result)
    {
        Console.WriteLine("------------------------------------------------------------");
        Console.WriteLine("                    Benchmark Results");
        Console.WriteLine("------------------------------------------------------------\n");

        var status = result.Passed ? "✅ Passed" : "❌ Failed";
        Console.WriteLine($"  Total Score: {result.FinalScore} / 100 ({result.FinalScore} points)");
        Console.WriteLine($"    - Tool Calling: {result.ToolScore}/80 ({result.ToolPassCount}/{result.ToolTotalCount} passed)");
        Console.WriteLine($"    - Vision Processing: {result.VisionScore}/20 ({result.VisionPassCount}/{result.VisionTotalCount} passed)");
        Console.WriteLine($"  Status: {status}\n");

        Console.WriteLine("  Details:");
        foreach (var caseResult in result.CaseResults)
        {
            var passMark = caseResult.Passed ? "✅" : "❌";
            Console.WriteLine($"    {passMark} {caseResult.CaseId,-20} {caseResult.CaseName,-15} [{caseResult.Category}]");
        }

        Console.WriteLine("\n------------------------------------------------------------");
        Console.WriteLine("                    Capability Assessment");
        Console.WriteLine("------------------------------------------------------------\n");

        var toolStatus = result.Capabilities.SupportsToolCalling ? "✅ Supported" : "❌ Not Supported";
        var visionStatus = result.Capabilities.SupportsVision ? "✅ Supported" : "❌ Not Supported";

        Console.WriteLine($"  Tool Calling: {toolStatus}");
        Console.WriteLine($"  Vision Processing: {visionStatus}");
        Console.WriteLine($"  Score: {result.FinalScore}/100");
    }

    private static async Task SaveAllCapabilitiesAsync(
        string configPath,
        List<(string ProfileName, BenchmarkResult Result)> results,
        CancellationToken cancellationToken)
    {
        try
        {
            // Read original file content
            var json = await File.ReadAllTextAsync(configPath, cancellationToken);
            var node = JsonNode.Parse(json);
            if (node == null)
            {
                Console.WriteLine($"\n⚠️ Failed to parse config file");
                return;
            }

            // Ensure llm.profiles exists
            var llm = node["llm"] ??= new JsonObject();
            var profiles = llm["profiles"] ??= new JsonObject();

            // Update each profile's capabilities
            foreach (var (profileName, result) in results)
            {
                var profile = profiles[profileName];
                if (profile == null)
                {
                    Console.WriteLine($"\n⚠️ Profile '{profileName}' not found");
                    continue;
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
            }

            // Serialize and save (preserve original format)
            var options = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            };
            var resultJson = JsonSerializer.Serialize(node, options);
            await File.WriteAllTextAsync(configPath, resultJson, cancellationToken);

            Console.WriteLine($"\n📝 Results saved to config file: {configPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n⚠️ Failed to save results: {ex.Message}");
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
            // Create directory structure: .benchmark/{profileName}/{timestamp}/
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

            Console.WriteLine($"📁 Detailed results saved to: {benchmarkDir}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Failed to save detailed results: {ex.Message}");
        }
    }
}