using Microsoft.Extensions.Logging;
using NanoBot.Core.Channels;
using NanoBot.Core.Configuration;
using NanoBot.Core.Services;

namespace NanoBot.Infrastructure.Services;

/// <summary>
/// Infrastructure implementation of channel configuration service.
/// </summary>
public class ChannelConfigService : IChannelConfigService
{
    private readonly string _configPath;
    private readonly ILogger<ChannelConfigService> _logger;

    public ChannelConfigService(ILogger<ChannelConfigService> logger)
    {
        _logger = logger;
        _configPath = GetConfigPath();
    }

    public async Task<ChannelsConfig?> GetChannelsConfigAsync()
    {
        try
        {
            if (!File.Exists(_configPath))
            {
                return new ChannelsConfig();
            }

            var config = await ConfigurationLoader.LoadAsync(_configPath);
            return config.Channels;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load channels config");
            return null;
        }
    }

    public async Task SaveChannelsConfigAsync(ChannelsConfig config)
    {
        try
        {
            AgentConfig? agentConfig;

            if (File.Exists(_configPath))
            {
                agentConfig = await ConfigurationLoader.LoadAsync(_configPath);
            }
            else
            {
                agentConfig = new AgentConfig();
            }

            agentConfig.Channels = config;
            await ConfigurationLoader.SaveAsync(_configPath, agentConfig);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save channels config");
            throw;
        }
    }

    public async Task<bool> IsChannelEnabledAsync(string channelId)
    {
        var config = await GetChannelsConfigAsync();
        if (config == null) return false;

        return channelId.ToLower() switch
        {
            "telegram" => config.Telegram?.Enabled ?? false,
            "discord" => config.Discord?.Enabled ?? false,
            "whatsapp" => config.WhatsApp?.Enabled ?? false,
            "slack" => config.Slack?.Enabled ?? false,
            "feishu" => config.Feishu?.Enabled ?? false,
            "dingtalk" => config.DingTalk?.Enabled ?? false,
            "email" => config.Email?.Enabled ?? false,
            "qq" => config.QQ?.Enabled ?? false,
            "matrix" => config.Matrix?.Enabled ?? false,
            _ => false
        };
    }

    public async Task EnableChannelAsync(string channelId)
    {
        var config = await GetChannelsConfigAsync();
        if (config == null) return;

        switch (channelId.ToLower())
        {
            case "telegram":
                config.Telegram ??= new TelegramConfig();
                config.Telegram.Enabled = true;
                break;
            case "discord":
                config.Discord ??= new DiscordConfig();
                config.Discord.Enabled = true;
                break;
            case "whatsapp":
                config.WhatsApp ??= new WhatsAppConfig();
                config.WhatsApp.Enabled = true;
                break;
            case "slack":
                config.Slack ??= new SlackConfig();
                config.Slack.Enabled = true;
                break;
            case "feishu":
                config.Feishu ??= new FeishuConfig();
                config.Feishu.Enabled = true;
                break;
            case "dingtalk":
                config.DingTalk ??= new DingTalkConfig();
                config.DingTalk.Enabled = true;
                break;
            case "email":
                config.Email ??= new EmailConfig();
                config.Email.Enabled = true;
                break;
            case "qq":
                config.QQ ??= new QQConfig();
                config.QQ.Enabled = true;
                break;
            case "matrix":
                config.Matrix ??= new MatrixConfig();
                config.Matrix.Enabled = true;
                break;
        }

        await SaveChannelsConfigAsync(config);
    }

    public async Task DisableChannelAsync(string channelId)
    {
        var config = await GetChannelsConfigAsync();
        if (config == null) return;

        switch (channelId.ToLower())
        {
            case "telegram":
                if (config.Telegram != null) config.Telegram.Enabled = false;
                break;
            case "discord":
                if (config.Discord != null) config.Discord.Enabled = false;
                break;
            case "whatsapp":
                if (config.WhatsApp != null) config.WhatsApp.Enabled = false;
                break;
            case "slack":
                if (config.Slack != null) config.Slack.Enabled = false;
                break;
            case "feishu":
                if (config.Feishu != null) config.Feishu.Enabled = false;
                break;
            case "dingtalk":
                if (config.DingTalk != null) config.DingTalk.Enabled = false;
                break;
            case "email":
                if (config.Email != null) config.Email.Enabled = false;
                break;
            case "qq":
                if (config.QQ != null) config.QQ.Enabled = false;
                break;
            case "matrix":
                if (config.Matrix != null) config.Matrix.Enabled = false;
                break;
        }

        await SaveChannelsConfigAsync(config);
    }

    public async Task<T?> GetChannelConfigAsync<T>(string channelId) where T : class, new()
    {
        var config = await GetChannelsConfigAsync();
        if (config == null) return null;

        return channelId.ToLower() switch
        {
            "telegram" => config.Telegram as T,
            "discord" => config.Discord as T,
            "whatsapp" => config.WhatsApp as T,
            "slack" => config.Slack as T,
            "feishu" => config.Feishu as T,
            "dingtalk" => config.DingTalk as T,
            "email" => config.Email as T,
            "qq" => config.QQ as T,
            "matrix" => config.Matrix as T,
            _ => null
        };
    }

    public async Task UpdateChannelConfigAsync<T>(string channelId, T channelConfig) where T : class
    {
        var config = await GetChannelsConfigAsync();
        if (config == null) return;

        switch (channelId.ToLower())
        {
            case "telegram":
                config.Telegram = channelConfig as TelegramConfig;
                break;
            case "discord":
                config.Discord = channelConfig as DiscordConfig;
                break;
            case "whatsapp":
                config.WhatsApp = channelConfig as WhatsAppConfig;
                break;
            case "slack":
                config.Slack = channelConfig as SlackConfig;
                break;
            case "feishu":
                config.Feishu = channelConfig as FeishuConfig;
                break;
            case "dingtalk":
                config.DingTalk = channelConfig as DingTalkConfig;
                break;
            case "email":
                config.Email = channelConfig as EmailConfig;
                break;
            case "qq":
                config.QQ = channelConfig as QQConfig;
                break;
            case "matrix":
                config.Matrix = channelConfig as MatrixConfig;
                break;
        }

        await SaveChannelsConfigAsync(config);
    }

    private static string GetConfigPath()
    {
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(homeDir, ".nbot", "config.json");
    }
}
