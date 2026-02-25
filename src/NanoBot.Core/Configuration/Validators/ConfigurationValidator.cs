using System.Text.RegularExpressions;

namespace NanoBot.Core.Configuration;

public static class ConfigurationValidator
{
    public static ValidationResult Validate(AgentConfig config)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        ValidateName(config, errors);
        ValidateWorkspace(config, errors, warnings);
        ValidateLlm(config, errors, warnings);
        ValidateSecurity(config, warnings);
        ValidateMemory(config, warnings);
        ValidateHeartbeat(config, warnings);
        ValidateMcp(config, warnings);
        ValidateChannels(config, warnings);

        return new ValidationResult(errors, warnings);
    }

    private static void ValidateName(AgentConfig config, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(config.Name))
        {
            errors.Add("Agent name is required");
        }
    }

    private static void ValidateWorkspace(AgentConfig config, List<string> errors, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(config.Workspace.Path))
        {
            errors.Add("Workspace path is required");
            return;
        }

        try
        {
            var resolvedPath = config.Workspace.GetResolvedPath();
            
            if (!Directory.Exists(resolvedPath))
            {
                warnings.Add($"Workspace directory does not exist: {resolvedPath}");
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Invalid workspace path: {ex.Message}");
        }
    }

    private static void ValidateLlm(AgentConfig config, List<string> errors, List<string> warnings)
    {
        var profileName = string.IsNullOrEmpty(config.Llm.DefaultProfile) ? "default" : config.Llm.DefaultProfile;
        
        if (!config.Llm.Profiles.TryGetValue(profileName, out var profile))
        {
            errors.Add($"LLM profile '{profileName}' not found in configuration");
            return;
        }

        if (string.IsNullOrWhiteSpace(profile.Model))
        {
            errors.Add($"LLM model is required in profile '{profileName}'");
        }

        var apiKey = profile.ApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY") 
            ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        
        if (string.IsNullOrWhiteSpace(apiKey) && profile.Provider != "ollama")
        {
            warnings.Add($"No API key configured for profile '{profileName}'. Set apiKey in config or via environment variable");
        }

        if (profile.Temperature < 0 || profile.Temperature > 2)
        {
            warnings.Add($"Temperature {profile.Temperature} is outside typical range [0, 2]");
        }

        if (profile.MaxTokens <= 0)
        {
            errors.Add("MaxTokens must be greater than 0");
        }

        if (!string.IsNullOrWhiteSpace(profile.ApiBase))
        {
            if (!Uri.TryCreate(profile.ApiBase, UriKind.Absolute, out _))
            {
                errors.Add($"Invalid API base URL: {profile.ApiBase}");
            }
        }
    }

    private static void ValidateSecurity(AgentConfig config, List<string> warnings)
    {
        if (!config.Security.RestrictToWorkspace && config.Security.AllowedDirs.Count == 0)
        {
            warnings.Add("RestrictToWorkspace is disabled but no AllowedDirs specified. Agent may access any directory.");
        }

        if (config.Security.ShellTimeout <= 0)
        {
            warnings.Add("ShellTimeout should be greater than 0");
        }

        foreach (var pattern in config.Security.DenyCommandPatterns)
        {
            try
            {
                _ = new Regex(pattern, RegexOptions.Compiled);
            }
            catch
            {
                warnings.Add($"Invalid deny command pattern: {pattern}");
            }
        }
    }

    private static void ValidateMemory(AgentConfig config, List<string> warnings)
    {
        if (config.Memory.Enabled)
        {
            if (config.Memory.MaxHistoryEntries <= 0)
            {
                warnings.Add("MaxHistoryEntries should be greater than 0 when memory is enabled");
            }
        }
    }

    private static void ValidateHeartbeat(AgentConfig config, List<string> warnings)
    {
        if (config.Heartbeat?.Enabled == true)
        {
            if (config.Heartbeat.IntervalSeconds <= 0)
            {
                warnings.Add("Heartbeat interval must be greater than 0");
            }
        }
    }

    private static void ValidateMcp(AgentConfig config, List<string> warnings)
    {
        if (config.Mcp?.Servers != null)
        {
            foreach (var (name, server) in config.Mcp.Servers)
            {
                if (string.IsNullOrWhiteSpace(server.Command))
                {
                    warnings.Add($"MCP server '{name}' has no command specified");
                }

                if (!string.IsNullOrWhiteSpace(server.Cwd) && !Directory.Exists(server.Cwd))
                {
                    warnings.Add($"MCP server '{name}' working directory does not exist: {server.Cwd}");
                }
            }
        }
    }

    private static void ValidateChannels(AgentConfig config, List<string> warnings)
    {
        ValidateTelegram(config.Channels.Telegram, warnings);
        ValidateDiscord(config.Channels.Discord, warnings);
        ValidateFeishu(config.Channels.Feishu, warnings);
        ValidateWhatsApp(config.Channels.WhatsApp, warnings);
        ValidateDingTalk(config.Channels.DingTalk, warnings);
        ValidateEmail(config.Channels.Email, warnings);
        ValidateSlack(config.Channels.Slack, warnings);
        ValidateQQ(config.Channels.QQ, warnings);
        ValidateMochat(config.Channels.Mochat, warnings);
    }

    private static void ValidateTelegram(TelegramConfig? config, List<string> warnings)
    {
        if (config?.Enabled == true && string.IsNullOrWhiteSpace(config.Token))
        {
            warnings.Add("Telegram is enabled but no token is configured");
        }
    }

    private static void ValidateDiscord(DiscordConfig? config, List<string> warnings)
    {
        if (config?.Enabled == true && string.IsNullOrWhiteSpace(config.Token))
        {
            warnings.Add("Discord is enabled but no token is configured");
        }
    }

    private static void ValidateFeishu(FeishuConfig? config, List<string> warnings)
    {
        if (config?.Enabled == true)
        {
            if (string.IsNullOrWhiteSpace(config.AppId) || string.IsNullOrWhiteSpace(config.AppSecret))
            {
                warnings.Add("Feishu is enabled but AppId or AppSecret is not configured");
            }
        }
    }

    private static void ValidateWhatsApp(WhatsAppConfig? config, List<string> warnings)
    {
        if (config?.Enabled == true && string.IsNullOrWhiteSpace(config.BridgeToken))
        {
            warnings.Add("WhatsApp is enabled but no bridge token is configured");
        }
    }

    private static void ValidateDingTalk(DingTalkConfig? config, List<string> warnings)
    {
        if (config?.Enabled == true)
        {
            if (string.IsNullOrWhiteSpace(config.ClientId) || string.IsNullOrWhiteSpace(config.ClientSecret))
            {
                warnings.Add("DingTalk is enabled but ClientId or ClientSecret is not configured");
            }
        }
    }

    private static void ValidateEmail(EmailConfig? config, List<string> warnings)
    {
        if (config?.Enabled == true)
        {
            if (!config.ConsentGranted)
            {
                warnings.Add("Email is enabled but consent is not granted");
            }

            if (string.IsNullOrWhiteSpace(config.ImapHost) || string.IsNullOrWhiteSpace(config.SmtpHost))
            {
                warnings.Add("Email is enabled but IMAP or SMTP host is not configured");
            }
        }
    }

    private static void ValidateSlack(SlackConfig? config, List<string> warnings)
    {
        if (config?.Enabled == true)
        {
            if (string.IsNullOrWhiteSpace(config.BotToken))
            {
                warnings.Add("Slack is enabled but no bot token is configured");
            }

            if (config.Mode == "socket" && string.IsNullOrWhiteSpace(config.AppToken))
            {
                warnings.Add("Slack socket mode requires AppToken");
            }
        }
    }

    private static void ValidateQQ(QQConfig? config, List<string> warnings)
    {
        if (config?.Enabled == true)
        {
            if (string.IsNullOrWhiteSpace(config.AppId) || string.IsNullOrWhiteSpace(config.Secret))
            {
                warnings.Add("QQ is enabled but AppId or Secret is not configured");
            }
        }
    }

    private static void ValidateMochat(MochatConfig? config, List<string> warnings)
    {
        if (config?.Enabled == true)
        {
            if (string.IsNullOrWhiteSpace(config.ClawToken))
            {
                warnings.Add("Mochat is enabled but no claw token is configured");
            }
        }
    }
}

public record ValidationResult(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool IsValid => Errors.Count == 0;
    
    public bool HasWarnings => Warnings.Count > 0;

    public string GetErrorMessage()
    {
        return Errors.Count > 0 
            ? string.Join(Environment.NewLine, Errors) 
            : string.Empty;
    }

    public string GetWarningMessage()
    {
        return Warnings.Count > 0 
            ? string.Join(Environment.NewLine, Warnings) 
            : string.Empty;
    }
}
