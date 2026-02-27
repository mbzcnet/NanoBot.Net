using FluentAssertions;
using NanoBot.Core.Configuration;
using Xunit;

namespace NanoBot.Core.Tests.Configuration;

public class ConfigurationTests
{
    [Fact]
    public void AgentConfig_ShouldHaveDefaultValues()
    {
        var config = new AgentConfig();

        config.Name.Should().Be("NanoBot");
        config.Workspace.Should().NotBeNull();
        config.Llm.Should().NotBeNull();
        config.Channels.Should().NotBeNull();
        config.Security.Should().NotBeNull();
        config.Memory.Should().NotBeNull();
        config.Mcp.Should().BeNull();
        config.Heartbeat.Should().BeNull();
    }

public class ConfigurationCheckerTests : IDisposable
{
    private readonly List<string> _tempDirs = new();
    private readonly string _originalCwd;

    public ConfigurationCheckerTests()
    {
        _originalCwd = Directory.GetCurrentDirectory();
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_originalCwd);
        foreach (var dir in _tempDirs)
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    [Fact]
    public void ResolveExistingConfigPath_ShouldFindProjectDotNbotConfigByWalkingUp()
    {
        var root = Path.Combine(Path.GetTempPath(), $"nbot_cfg_{Guid.NewGuid():N}");
        var child = Path.Combine(root, "a", "b", "c");
        Directory.CreateDirectory(child);
        _tempDirs.Add(root);

        var cfgDir = Path.Combine(root, ".nbot");
        Directory.CreateDirectory(cfgDir);
        var cfgPath = Path.Combine(cfgDir, "config.json");
        File.WriteAllText(cfgPath, "{}");

        Directory.SetCurrentDirectory(child);

        var resolved = ConfigurationChecker.ResolveExistingConfigPath(null);
        resolved.Should().NotBeNull();
        NormalizePath(resolved!).Should().Be(NormalizePath(cfgPath));
    }

    private static string NormalizePath(string path)
    {
        var full = Path.GetFullPath(path);
        const string privatePrefix = "/private";
        if (full.StartsWith(privatePrefix + "/", StringComparison.Ordinal))
        {
            return full[privatePrefix.Length..];
        }
        return full;
    }
}

    [Fact]
    public void WorkspaceConfig_DefaultPath_ShouldBeDotNbot()
    {
        var workspace = new WorkspaceConfig();

        workspace.Path.Should().Be(".nbot");
    }

    [Fact]
    public void WorkspaceConfig_ShouldResolveHomePath()
    {
        var workspace = new WorkspaceConfig
        {
            Path = "~/test/workspace"
        };

        var resolvedPath = workspace.GetResolvedPath();

        resolvedPath.Should().NotContain("~");
        resolvedPath.Should().EndWith("test/workspace");
    }

    [Fact]
    public void WorkspaceConfig_ShouldResolveRelativePathAgainstCurrentDirectory()
    {
        var workspace = new WorkspaceConfig { Path = ".nbot" };

        var resolvedPath = workspace.GetResolvedPath();

        resolvedPath.Should().EndWith(".nbot");
        resolvedPath.Should().Be(Path.GetFullPath(".nbot"));
    }

    [Fact]
    public void WorkspaceConfig_ShouldReturnCorrectSubPaths()
    {
        var workspace = new WorkspaceConfig
        {
            Path = "/tmp/test-workspace"
        };

        workspace.GetMemoryPath().Should().EndWith("memory");
        workspace.GetSkillsPath().Should().EndWith("skills");
        workspace.GetSessionsPath().Should().EndWith("sessions");
        workspace.GetAgentsFile().Should().EndWith("AGENTS.md");
        workspace.GetSoulFile().Should().EndWith("SOUL.md");
        workspace.GetToolsFile().Should().EndWith("TOOLS.md");
        workspace.GetUserFile().Should().EndWith("USER.md");
        workspace.GetHeartbeatFile().Should().EndWith("HEARTBEAT.md");
    }

    [Fact]
    public void LlmConfig_ShouldHaveDefaultValues()
    {
        var llm = new LlmConfig();

        llm.DefaultProfile.Should().Be("default");
        llm.Profiles.Should().NotBeNull();
        llm.Profiles.Should().ContainKey("default");
        llm.Profiles["default"].Temperature.Should().Be(0.1);
        llm.Profiles["default"].MaxTokens.Should().Be(4096);
    }

    [Fact]
    public void LlmProfile_ShouldHaveDefaultValues()
    {
        var profile = new LlmProfile();

        profile.Name.Should().Be("default");
        profile.Model.Should().BeEmpty();
        profile.ApiKey.Should().BeNull();
        profile.ApiBase.Should().BeNull();
        profile.Provider.Should().BeNull();
        profile.Temperature.Should().Be(0.1);
        profile.MaxTokens.Should().Be(4096);
        profile.SystemPrompt.Should().BeNull();
    }

    [Fact]
    public void SecurityConfig_ShouldHaveDefaultValues()
    {
        var security = new SecurityConfig();

        security.AllowedDirs.Should().BeEmpty();
        security.DenyCommandPatterns.Should().BeEmpty();
        security.RestrictToWorkspace.Should().BeTrue();
        security.ShellTimeout.Should().Be(60);
    }

    [Fact]
    public void MemoryConfig_ShouldHaveDefaultValues()
    {
        var memory = new MemoryConfig();

        memory.MemoryFile.Should().Be("MEMORY.md");
        memory.HistoryFile.Should().Be("HISTORY.md");
        memory.MaxHistoryEntries.Should().Be(500);
        memory.Enabled.Should().BeTrue();
    }

    [Fact]
    public void HeartbeatConfig_ShouldHaveDefaultValues()
    {
        var heartbeat = new HeartbeatConfig();

        heartbeat.Enabled.Should().BeFalse();
        heartbeat.IntervalSeconds.Should().Be(300);
        heartbeat.Message.Should().BeNull();
    }

    [Fact]
    public void McpConfig_ShouldInitializeWithEmptyServers()
    {
        var mcp = new McpConfig();

        mcp.Servers.Should().NotBeNull();
        mcp.Servers.Should().BeEmpty();
    }

    [Fact]
    public void McpServerConfig_ShouldHaveDefaultValues()
    {
        var server = new McpServerConfig();

        server.Command.Should().BeEmpty();
        server.Args.Should().BeEmpty();
        server.Env.Should().NotBeNull();
        server.Cwd.Should().BeNull();
    }

    [Fact]
    public void ChannelsConfig_ShouldInitializeAllChannelsAsNull()
    {
        var channels = new ChannelsConfig();

        channels.Telegram.Should().BeNull();
        channels.Discord.Should().BeNull();
        channels.Feishu.Should().BeNull();
        channels.WhatsApp.Should().BeNull();
        channels.DingTalk.Should().BeNull();
        channels.Email.Should().BeNull();
        channels.Slack.Should().BeNull();
        channels.QQ.Should().BeNull();
        channels.Mochat.Should().BeNull();
    }
}

public class ChannelConfigTests
{
    [Fact]
    public void TelegramConfig_ShouldHaveDefaultValues()
    {
        var config = new TelegramConfig();

        config.Enabled.Should().BeFalse();
        config.Token.Should().BeEmpty();
        config.AllowFrom.Should().BeEmpty();
        config.Proxy.Should().BeNull();
    }

    [Fact]
    public void DiscordConfig_ShouldHaveDefaultValues()
    {
        var config = new DiscordConfig();

        config.Enabled.Should().BeFalse();
        config.Token.Should().BeEmpty();
        config.AllowFrom.Should().BeEmpty();
        config.GatewayUrl.Should().Be("wss://gateway.discord.gg/?v=10&encoding=json");
        config.Intents.Should().Be(37377);
    }

    [Fact]
    public void FeishuConfig_ShouldHaveDefaultValues()
    {
        var config = new FeishuConfig();

        config.Enabled.Should().BeFalse();
        config.AppId.Should().BeEmpty();
        config.AppSecret.Should().BeEmpty();
        config.EncryptKey.Should().BeEmpty();
        config.VerificationToken.Should().BeEmpty();
        config.AllowFrom.Should().BeEmpty();
    }

    [Fact]
    public void WhatsAppConfig_ShouldHaveDefaultValues()
    {
        var config = new WhatsAppConfig();

        config.Enabled.Should().BeFalse();
        config.BridgeUrl.Should().Be("ws://localhost:3001");
        config.BridgeToken.Should().BeEmpty();
        config.AllowFrom.Should().BeEmpty();
    }

    [Fact]
    public void DingTalkConfig_ShouldHaveDefaultValues()
    {
        var config = new DingTalkConfig();

        config.Enabled.Should().BeFalse();
        config.ClientId.Should().BeEmpty();
        config.ClientSecret.Should().BeEmpty();
        config.AllowFrom.Should().BeEmpty();
    }

    [Fact]
    public void EmailConfig_ShouldHaveDefaultValues()
    {
        var config = new EmailConfig();

        config.Enabled.Should().BeFalse();
        config.ConsentGranted.Should().BeFalse();
        config.ImapPort.Should().Be(993);
        config.ImapMailbox.Should().Be("INBOX");
        config.ImapUseSsl.Should().BeTrue();
        config.SmtpPort.Should().Be(587);
        config.SmtpUseTls.Should().BeTrue();
        config.AutoReplyEnabled.Should().BeTrue();
        config.PollIntervalSeconds.Should().Be(30);
        config.MarkSeen.Should().BeTrue();
        config.MaxBodyChars.Should().Be(12000);
    }

    [Fact]
    public void SlackConfig_ShouldHaveDefaultValues()
    {
        var config = new SlackConfig();

        config.Enabled.Should().BeFalse();
        config.Mode.Should().Be("socket");
        config.BotToken.Should().BeEmpty();
        config.AppToken.Should().BeEmpty();
        config.GroupPolicy.Should().Be("mention");
        config.GroupAllowFrom.Should().BeEmpty();
        config.Dm.Should().NotBeNull();
    }

    [Fact]
    public void SlackDmConfig_ShouldHaveDefaultValues()
    {
        var config = new SlackDmConfig();

        config.Enabled.Should().BeTrue();
        config.Policy.Should().Be("open");
        config.AllowFrom.Should().BeEmpty();
    }

    [Fact]
    public void QQConfig_ShouldHaveDefaultValues()
    {
        var config = new QQConfig();

        config.Enabled.Should().BeFalse();
        config.AppId.Should().BeEmpty();
        config.Secret.Should().BeEmpty();
        config.AllowFrom.Should().BeEmpty();
    }

    [Fact]
    public void MochatConfig_ShouldHaveDefaultValues()
    {
        var config = new MochatConfig();

        config.Enabled.Should().BeFalse();
        config.BaseUrl.Should().Be("https://mochat.io");
        config.SocketUrl.Should().BeEmpty();
        config.SocketPath.Should().Be("/socket.io");
        config.ClawToken.Should().BeEmpty();
        config.AgentUserId.Should().BeEmpty();
        config.Sessions.Should().BeEmpty();
        config.Panels.Should().BeEmpty();
        config.AllowFrom.Should().BeEmpty();
        config.Mention.Should().NotBeNull();
        config.ReplyDelayMode.Should().Be("non-mention");
        config.ReplyDelayMs.Should().Be(120000);
    }

    [Fact]
    public void MochatMentionConfig_ShouldHaveDefaultValues()
    {
        var config = new MochatMentionConfig();

        config.RequireInGroups.Should().BeFalse();
    }
}

public class ConfigurationLoaderTests : IDisposable
{
    private readonly List<string> _tempFiles = new();
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            if (File.Exists(file))
                File.Delete(file);
        }
        foreach (var dir in _tempDirs)
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    private string CreateTempConfigFile(string content)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"config_{Guid.NewGuid()}.json");
        File.WriteAllText(tempPath, content);
        _tempFiles.Add(tempPath);
        return tempPath;
    }

    [Fact]
    public async Task LoadAsync_ShouldLoadValidConfig()
    {
        var json = "{\"name\":\"TestBot\",\"workspace\":{\"path\":\"/tmp/test-workspace\"},\"llm\":{\"profiles\":{\"default\":{\"model\":\"gpt-4\",\"api_key\":\"test-key\",\"temperature\":0.5,\"max_tokens\":2048}}}}";

        var configPath = CreateTempConfigFile(json);
        var config = await ConfigurationLoader.LoadAsync(configPath);

        config.Name.Should().Be("TestBot");
        config.Workspace.Path.Should().Be("/tmp/test-workspace");
        config.Llm.Profiles["default"].Model.Should().Be("gpt-4");
        config.Llm.Profiles["default"].Temperature.Should().Be(0.5);
        config.Llm.Profiles["default"].MaxTokens.Should().Be(2048);
    }

    [Fact]
    public async Task LoadAsync_ShouldNotMisclassifySnakeCaseConfigWithChannelsAsNanobotConfig()
    {
        var json = "{\"name\":\"TestBot\",\"llm\":{\"default_profile\":\"default\",\"profiles\":{\"default\":{\"provider\":\"openai\",\"model\":\"qwen-plus\",\"api_key\":\"test-key\",\"api_base\":\"https://dashscope.aliyuncs.com/compatible-mode/v1\",\"temperature\":0.5,\"max_tokens\":64096}}},\"channels\":{\"telegram\":{\"enabled\":false,\"token\":\"x\"}}}";

        var configPath = CreateTempConfigFile(json);
        var config = await ConfigurationLoader.LoadAsync(configPath);

        config.Llm.DefaultProfile.Should().Be("default");
        config.Llm.Profiles["default"].Model.Should().Be("qwen-plus");
        config.Llm.Profiles["default"].ApiKey.Should().Be("test-key");
    }

    [Fact]
    public async Task LoadAsync_ShouldLoadPascalCaseConfig()
    {
        var json = "{\"Name\":\"TestBot\",\"Workspace\":{\"Path\":\"/tmp/test-workspace\"},\"Llm\":{\"DefaultProfile\":\"default\",\"Profiles\":{\"default\":{\"Provider\":\"openai\",\"Model\":\"gpt-4o-mini\",\"ApiKey\":\"test-key\",\"ApiBase\":\"https://api.openai.com/v1\"}}}}";

        var configPath = CreateTempConfigFile(json);
        var config = await ConfigurationLoader.LoadAsync(configPath);

        config.Name.Should().Be("TestBot");
        config.Workspace.Path.Should().Be("/tmp/test-workspace");
        config.Llm.DefaultProfile.Should().Be("default");
        config.Llm.Profiles["default"].Provider.Should().Be("openai");
        config.Llm.Profiles["default"].Model.Should().Be("gpt-4o-mini");
        config.Llm.Profiles["default"].ApiKey.Should().Be("test-key");
        config.Llm.Profiles["default"].ApiBase.Should().Be("https://api.openai.com/v1");
    }

    [Fact]
    public async Task LoadAsync_ShouldLoadNanobotConfigShape()
    {
        var json = "{\"agents\":{\"defaults\":{\"workspace\":\"/tmp/test-workspace\",\"model\":\"openai/gpt-4o-mini\",\"temperature\":0.6,\"max_tokens\":1234}},\"providers\":{\"openai\":{\"api_key\":\"test-key\",\"api_base\":\"https://api.openai.com/v1\"}}}";

        var configPath = CreateTempConfigFile(json);
        var config = await ConfigurationLoader.LoadAsync(configPath);

        config.Workspace.Path.Should().Be("/tmp/test-workspace");
        config.Llm.Profiles["default"].Model.Should().Be("openai/gpt-4o-mini");
        config.Llm.Profiles["default"].Provider.Should().Be("openai");
        config.Llm.Profiles["default"].ApiKey.Should().Be("test-key");
        config.Llm.Profiles["default"].ApiBase.Should().Be("https://api.openai.com/v1");
        config.Llm.Profiles["default"].Temperature.Should().Be(0.6);
        config.Llm.Profiles["default"].MaxTokens.Should().Be(1234);
    }

    [Fact]
    public async Task LoadAsync_ShouldLoadNanobotCamelCaseConfigShape()
    {
        var json = "{\"agents\":{\"defaults\":{\"workspace\":\"/tmp/test-workspace\",\"model\":\"openai/gpt-4o-mini\",\"temperature\":0.6,\"maxTokens\":1234}},\"providers\":{\"openai\":{\"apiKey\":\"test-key\",\"apiBase\":\"https://api.openai.com/v1\"}}}";

        var configPath = CreateTempConfigFile(json);
        var config = await ConfigurationLoader.LoadAsync(configPath);

        config.Workspace.Path.Should().Be("/tmp/test-workspace");
        config.Llm.Profiles["default"].Model.Should().Be("openai/gpt-4o-mini");
        config.Llm.Profiles["default"].Provider.Should().Be("openai");
        config.Llm.Profiles["default"].ApiKey.Should().Be("test-key");
        config.Llm.Profiles["default"].ApiBase.Should().Be("https://api.openai.com/v1");
        config.Llm.Profiles["default"].Temperature.Should().Be(0.6);
        config.Llm.Profiles["default"].MaxTokens.Should().Be(1234);
    }

    [Fact]
    public async Task LoadAsync_ShouldLoadMixedCaseConfig()
    {
        var json = "{\"llm\":{\"DefaultProfile\":\"default\",\"profiles\":{\"default\":{\"Model\":\"qwen-plus\",\"ApiKey\":\"test-key\",\"ApiBase\":\"https://dashscope.aliyuncs.com/compatible-mode/v1\",\"Provider\":\"openai\",\"Temperature\":0.5,\"MaxTokens\":64096}}}}";

        var configPath = CreateTempConfigFile(json);
        var config = await ConfigurationLoader.LoadAsync(configPath);

        config.Llm.DefaultProfile.Should().Be("default");
        config.Llm.Profiles["default"].Model.Should().Be("qwen-plus");
        config.Llm.Profiles["default"].ApiKey.Should().Be("test-key");
        config.Llm.Profiles["default"].ApiBase.Should().Be("https://dashscope.aliyuncs.com/compatible-mode/v1");
        config.Llm.Profiles["default"].Provider.Should().Be("openai");
        config.Llm.Profiles["default"].Temperature.Should().Be(0.5);
        config.Llm.Profiles["default"].MaxTokens.Should().Be(64096);
    }

    [Fact]
    public void Load_ShouldLoadValidConfig()
    {
        var json = "{\"name\":\"SyncBot\",\"llm\":{\"profiles\":{\"default\":{\"model\":\"claude-3\"}}}}";

        var configPath = CreateTempConfigFile(json);
        var config = ConfigurationLoader.Load(configPath);

        config.Name.Should().Be("SyncBot");
        config.Llm.Profiles["default"].Model.Should().Be("claude-3");
    }

    [Fact]
    public void ReplaceEnvironmentVariables_ShouldReplaceVariables()
    {
        Environment.SetEnvironmentVariable("TEST_API_KEY", "secret-key-123");
        
        var content = "{\"api_key\": \"${TEST_API_KEY}\"}";
        var result = ConfigurationLoader.ReplaceEnvironmentVariables(content);

        result.Should().Contain("secret-key-123");
        result.Should().NotContain("${TEST_API_KEY}");
        
        Environment.SetEnvironmentVariable("TEST_API_KEY", null);
    }

    [Fact]
    public void ReplaceEnvironmentVariables_ShouldHandleMissingVariables()
    {
        var content = "{\"api_key\": \"${NONEXISTENT_VAR}\"}";
        var result = ConfigurationLoader.ReplaceEnvironmentVariables(content);

        result.Should().Contain("\": \"\"");
    }

    [Fact]
    public async Task LoadWithDefaultsAsync_ShouldReturnDefaultWhenNoConfigExists()
    {
        var config = await ConfigurationLoader.LoadWithDefaultsAsync("/nonexistent/path/config.json");

        config.Should().NotBeNull();
        config.Name.Should().Be("NanoBot");
    }

    [Fact]
    public async Task SaveAsync_ShouldSaveConfigCorrectly()
    {
        var config = new AgentConfig
        {
            Name = "SaveTestBot",
            Llm = new LlmConfig
            {
                Profiles = new Dictionary<string, LlmProfile>
                {
                    ["default"] = new LlmProfile { Model = "test-model" }
                }
            }
        };

        var tempPath = Path.Combine(Path.GetTempPath(), $"save_test_{Guid.NewGuid()}.json");
        _tempFiles.Add(tempPath);

        await ConfigurationLoader.SaveAsync(tempPath, config);

        File.Exists(tempPath).Should().BeTrue();
        var savedContent = await File.ReadAllTextAsync(tempPath);
        savedContent.Should().Contain("SaveTestBot");
        savedContent.Should().Contain("test-model");
    }
}

public class ConfigurationValidatorTests
{
    private static LlmConfig CreateLlmConfig(string? provider = null, string? model = null, string? apiKey = null, double? temperature = null, int? maxTokens = null)
    {
        return new LlmConfig
        {
            Profiles = new Dictionary<string, LlmProfile>
            {
                ["default"] = new LlmProfile
                {
                    Provider = provider ?? "openai",
                    Model = model ?? "gpt-4",
                    ApiKey = apiKey,
                    Temperature = temperature ?? 0.7,
                    MaxTokens = maxTokens ?? 4096
                }
            }
        };
    }

    [Fact]
    public void Validate_ShouldReturnErrorsForInvalidConfig()
    {
        var config = new AgentConfig
        {
            Name = "",
            Llm = CreateLlmConfig(model: "")
        };

        var result = ConfigurationValidator.Validate(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ShouldReturnWarningsForMissingApiKey()
    {
        var config = new AgentConfig
        {
            Name = "TestBot",
            Llm = CreateLlmConfig(apiKey: null)
        };

        var result = ConfigurationValidator.Validate(config);

        result.HasWarnings.Should().BeTrue();
        result.Warnings.Should().Contain(w => w.Contains("API key", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ShouldPassForValidConfig()
    {
        var config = new AgentConfig
        {
            Name = "ValidBot",
            Workspace = new WorkspaceConfig { Path = "/tmp/workspace" },
            Llm = CreateLlmConfig(apiKey: "test-key")
        };

        var result = ConfigurationValidator.Validate(config);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ShouldWarnAboutDisabledWorkspaceRestriction()
    {
        var config = new AgentConfig
        {
            Name = "TestBot",
            Llm = CreateLlmConfig(apiKey: "key"),
            Security = new SecurityConfig { RestrictToWorkspace = false }
        };

        var result = ConfigurationValidator.Validate(config);

        result.Warnings.Should().Contain(w => w.Contains("RestrictToWorkspace"));
    }

    [Fact]
    public void Validate_ShouldValidateTelegramConfig()
    {
        var config = new AgentConfig
        {
            Name = "TestBot",
            Llm = CreateLlmConfig(apiKey: "key"),
            Channels = new ChannelsConfig
            {
                Telegram = new TelegramConfig { Enabled = true, Token = "" }
            }
        };

        var result = ConfigurationValidator.Validate(config);

        result.Warnings.Should().Contain(w => w.Contains("Telegram") && w.Contains("token"));
    }

    [Fact]
    public void Validate_ShouldValidateDiscordConfig()
    {
        var config = new AgentConfig
        {
            Name = "TestBot",
            Llm = CreateLlmConfig(apiKey: "key"),
            Channels = new ChannelsConfig
            {
                Discord = new DiscordConfig { Enabled = true, Token = "" }
            }
        };

        var result = ConfigurationValidator.Validate(config);

        result.Warnings.Should().Contain(w => w.Contains("Discord") && w.Contains("token"));
    }

    [Fact]
    public void Validate_ShouldValidateSlackSocketMode()
    {
        var config = new AgentConfig
        {
            Name = "TestBot",
            Llm = CreateLlmConfig(apiKey: "key"),
            Channels = new ChannelsConfig
            {
                Slack = new SlackConfig 
                { 
                    Enabled = true, 
                    Mode = "socket",
                    BotToken = "xoxb-test",
                    AppToken = ""
                }
            }
        };

        var result = ConfigurationValidator.Validate(config);

        result.Warnings.Should().Contain(w => w.Contains("socket mode") && w.Contains("AppToken"));
    }

    [Fact]
    public void Validate_ShouldValidateTemperatureRange()
    {
        var config = new AgentConfig
        {
            Name = "TestBot",
            Llm = CreateLlmConfig(apiKey: "key", temperature: 3.0)
        };

        var result = ConfigurationValidator.Validate(config);

        result.Warnings.Should().Contain(w => w.Contains("Temperature"));
    }

    [Fact]
    public void Validate_ShouldValidateMaxTokens()
    {
        var config = new AgentConfig
        {
            Name = "TestBot",
            Llm = CreateLlmConfig(apiKey: "key", maxTokens: 0)
        };

        var result = ConfigurationValidator.Validate(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("MaxTokens"));
    }

    [Fact]
    public void ValidationResult_ShouldProvideMessages()
    {
        var errors = new List<string> { "Error 1", "Error 2" };
        var warnings = new List<string> { "Warning 1" };

        var result = new ValidationResult(errors, warnings);

        result.IsValid.Should().BeFalse();
        result.HasWarnings.Should().BeTrue();
        result.GetErrorMessage().Should().Contain("Error 1");
        result.GetWarningMessage().Should().Contain("Warning 1");
    }

    [Fact]
    public void ConfigurationCheckResult_GetGuidanceMessage_ShouldRecommendOnboardWhenConfigMissing()
    {
        var result = new ConfigurationCheckResult { ConfigExists = false };

        var message = result.GetGuidanceMessage();

        message.Should().Contain("nbot onboard");
        message.Should().NotContain("nbot configure");
    }

    [Fact]
    public void ConfigurationCheckResult_GetGuidanceMessage_ShouldRecommendOnboardWhenLlmMissing()
    {
        var result = new ConfigurationCheckResult { ConfigExists = true, HasValidLlm = false };

        var message = result.GetGuidanceMessage();

        message.Should().Contain("nbot onboard");
    }

    [Fact]
    public void ConfigurationCheckResult_GetGuidanceMessage_ShouldRecommendOnboardWhenApiKeyMissing()
    {
        var result = new ConfigurationCheckResult { ConfigExists = true, HasValidLlm = true, HasApiKey = false };

        var message = result.GetGuidanceMessage();

        message.Should().Contain("nbot onboard");
    }

    [Fact]
    public void ConfigurationCheckResult_GetGuidanceMessage_ShouldReturnEmptyWhenReady()
    {
        var result = new ConfigurationCheckResult { ConfigExists = true, HasValidLlm = true, HasApiKey = true };

        var message = result.GetGuidanceMessage();

        message.Should().BeEmpty();
    }
}
