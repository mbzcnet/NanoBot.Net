using System.CommandLine;
using System.Text.Json;
using FluentAssertions;
using NanoBot.Cli.Commands;
using NanoBot.Core.Configuration;
using Xunit;

namespace NanoBot.Cli.Tests;

public class OnboardCommandTests
{
    [Fact]
    public async Task Onboard_NonInteractive_ShouldCreateConfigAndWorkspaceInTempDir()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "nbot_onboard_test_" + Guid.NewGuid().ToString("N")[..8]);
        var originalCwd = Environment.CurrentDirectory;
        var originalHome = Environment.GetEnvironmentVariable("HOME");
        var originalUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");

        try
        {
            Directory.CreateDirectory(tempDir);
            Environment.CurrentDirectory = tempDir;
            Environment.SetEnvironmentVariable("HOME", tempDir);
            Environment.SetEnvironmentVariable("USERPROFILE", tempDir);

            var onboard = new OnboardCommand();
            var command = onboard.CreateCommand();
            var root = new RootCommand("test");
            root.AddCommand(command);

            var exitCode = await root.InvokeAsync(new[] { "onboard", "--non-interactive" });

            exitCode.Should().Be(0);

            var configPath = Path.Combine(tempDir, ".nbot", "config.json");
            File.Exists(configPath).Should().BeTrue();
            var configJson = await File.ReadAllTextAsync(configPath);
            var doc = JsonDocument.Parse(configJson);
            doc.RootElement.TryGetProperty("workspace", out var workspaceEl).Should().BeTrue();
            workspaceEl.TryGetProperty("path", out var pathEl).Should().BeTrue();
            pathEl.GetString().Should().Be(".nbot");

            var workspaceDir = Path.Combine(tempDir, ".nbot");
            Directory.Exists(workspaceDir).Should().BeTrue();
            File.Exists(Path.Combine(workspaceDir, "AGENTS.md")).Should().BeTrue();
            File.Exists(Path.Combine(workspaceDir, "SOUL.md")).Should().BeTrue();
            File.Exists(Path.Combine(workspaceDir, "USER.md")).Should().BeTrue();
            File.Exists(Path.Combine(workspaceDir, "memory", "MEMORY.md")).Should().BeTrue();
            File.Exists(Path.Combine(workspaceDir, "memory", "HISTORY.md")).Should().BeTrue();
            Directory.Exists(Path.Combine(workspaceDir, "skills")).Should().BeTrue();
        }
        finally
        {
            Environment.CurrentDirectory = originalCwd;
            Environment.SetEnvironmentVariable("HOME", originalHome ?? "");
            Environment.SetEnvironmentVariable("USERPROFILE", originalUserProfile ?? "");
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch
                {
                    // ignore cleanup errors
                }
            }
        }
    }

    [Fact]
    public async Task Onboard_NonInteractive_WithProviderAndApiKey_ShouldPersistLlmInConfig()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "nbot_onboard_llm_" + Guid.NewGuid().ToString("N")[..8]);
        var originalCwd = Environment.CurrentDirectory;
        var originalHome = Environment.GetEnvironmentVariable("HOME");
        var originalUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");

        try
        {
            Directory.CreateDirectory(tempDir);
            Environment.CurrentDirectory = tempDir;
            Environment.SetEnvironmentVariable("HOME", tempDir);
            Environment.SetEnvironmentVariable("USERPROFILE", tempDir);

            var onboard = new OnboardCommand();
            var command = onboard.CreateCommand();
            var root = new RootCommand("test");
            root.AddCommand(command);

            var exitCode = await root.InvokeAsync(new[]
            {
                "onboard",
                "--non-interactive",
                "--provider", "openai",
                "--model", "gpt-4o-mini",
                "--api-key", "test-key-12345",
                "--api-base", "https://custom.example.com/v1"
            });

            exitCode.Should().Be(0);

            var configPath = Path.Combine(tempDir, ".nbot", "config.json");
            File.Exists(configPath).Should().BeTrue();
            var config = await ConfigurationLoader.LoadAsync(configPath);
            var profile = config.Llm.Profiles["default"];
            profile.Provider.Should().Be("openai");
            profile.Model.Should().Be("gpt-4o-mini");
            profile.ApiKey.Should().Be("test-key-12345");
            profile.ApiBase.Should().Be("https://custom.example.com/v1");
        }
        finally
        {
            Environment.CurrentDirectory = originalCwd;
            Environment.SetEnvironmentVariable("HOME", originalHome ?? "");
            Environment.SetEnvironmentVariable("USERPROFILE", originalUserProfile ?? "");
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}
