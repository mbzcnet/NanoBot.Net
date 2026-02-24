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

        llm.Model.Should().BeEmpty();
        llm.Temperature.Should().Be(0.7);
        llm.MaxTokens.Should().Be(4096);
        llm.ApiKey.Should().BeNull();
        llm.ApiBase.Should().BeNull();
        llm.Provider.Should().BeNull();
        llm.SystemPrompt.Should().BeNull();
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
        var json = "{\"name\":\"TestBot\",\"workspace\":{\"path\":\"/tmp/test-workspace\"},\"llm\":{\"model\":\"gpt-4\",\"api_key\":\"test-key\",\"temperature\":0.5,\"max_tokens\":2048}}";

        var configPath = CreateTempConfigFile(json);
        var config = await ConfigurationLoader.LoadAsync(configPath);

        config.Name.Should().Be("TestBot");
        config.Workspace.Path.Should().Be("/tmp/test-workspace");
        config.Llm.Model.Should().Be("gpt-4");
        config.Llm.Temperature.Should().Be(0.5);
        config.Llm.MaxTokens.Should().Be(2048);
    }

    [Fact]
    public void Load_ShouldLoadValidConfig()
    {
        var json = "{\"name\":\"SyncBot\",\"llm\":{\"model\":\"claude-3\"}}";

        var configPath = CreateTempConfigFile(json);
        var config = ConfigurationLoader.Load(configPath);

        config.Name.Should().Be("SyncBot");
        config.Llm.Model.Should().Be("claude-3");
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
            Llm = new LlmConfig { Model = "test-model" }
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
    [Fact]
    public void Validate_ShouldReturnErrorsForInvalidConfig()
    {
        var config = new AgentConfig
        {
            Name = "",
            Llm = new LlmConfig { Model = "" }
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
            Llm = new LlmConfig { Model = "gpt-4" }
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
            Llm = new LlmConfig 
            { 
                Model = "gpt-4",
                ApiKey = "test-key"
            }
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
            Llm = new LlmConfig { Model = "gpt-4", ApiKey = "key" },
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
            Llm = new LlmConfig { Model = "gpt-4", ApiKey = "key" },
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
            Llm = new LlmConfig { Model = "gpt-4", ApiKey = "key" },
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
            Llm = new LlmConfig { Model = "gpt-4", ApiKey = "key" },
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
            Llm = new LlmConfig { Model = "gpt-4", ApiKey = "key", Temperature = 3.0 }
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
            Llm = new LlmConfig { Model = "gpt-4", ApiKey = "key", MaxTokens = 0 }
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
