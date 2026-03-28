using System.Runtime.CompilerServices;
using MudBlazor;
using NanoBot.Core.Channels;
using NanoBot.Core.Configuration;

namespace NanoBot.WebUI.Services;

/// <summary>
/// Holds a mutable reference to a channel config object so closures can update it.
/// </summary>
public sealed class ChannelConfigRef
{
    public object Config { get; set; }
    public ChannelConfigRef(object config) => Config = config;
}

/// <summary>
/// Static helpers for channel configuration UI.
/// </summary>
public static class ChannelFormattingService
{
    /// <summary>
    /// Converts a system image name to a MudBlazor icon.
    /// </summary>
    public static string GetChannelIcon(string systemImage)
    {
        return systemImage switch
        {
            "paperplane" => Icons.Material.Filled.Send,
            "message" => Icons.Material.Filled.Message,
            "bubble.left.and.bubble.right" => Icons.Material.Filled.Chat,
            "number" => Icons.Material.Filled.Tag,
            "work" => Icons.Material.Filled.Work,
            "mail" => Icons.Material.Filled.Email,
            "chat" => Icons.Material.Filled.Forum,
            "network" => Icons.Material.Filled.Hub,
            _ => Icons.Material.Filled.Extension
        };
    }

    /// <summary>
    /// Returns true if the given channel is enabled in the config.
    /// </summary>
    public static bool IsChannelEnabled(AgentConfig config, string channelId)
    {
        return channelId.ToLower() switch
        {
            "telegram" => config.Channels?.Telegram?.Enabled ?? false,
            "discord" => config.Channels?.Discord?.Enabled ?? false,
            "whatsapp" => config.Channels?.WhatsApp?.Enabled ?? false,
            "slack" => config.Channels?.Slack?.Enabled ?? false,
            "feishu" => config.Channels?.Feishu?.Enabled ?? false,
            "dingtalk" => config.Channels?.DingTalk?.Enabled ?? false,
            "email" => config.Channels?.Email?.Enabled ?? false,
            "qq" => config.Channels?.QQ?.Enabled ?? false,
            "matrix" => config.Channels?.Matrix?.Enabled ?? false,
            _ => false
        };
    }

    /// <summary>
    /// Ensures a channel is enabled in the config.
    /// </summary>
    public static void EnableChannel(AgentConfig config, string channelId)
    {
        config.Channels ??= new ChannelsConfig();
        switch (channelId.ToLower())
        {
            case "telegram": config.Channels.Telegram ??= new TelegramConfig(); config.Channels.Telegram.Enabled = true; break;
            case "discord": config.Channels.Discord ??= new DiscordConfig(); config.Channels.Discord.Enabled = true; break;
            case "whatsapp": config.Channels.WhatsApp ??= new WhatsAppConfig(); config.Channels.WhatsApp.Enabled = true; break;
            case "slack": config.Channels.Slack ??= new SlackConfig(); config.Channels.Slack.Enabled = true; break;
            case "feishu": config.Channels.Feishu ??= new FeishuConfig(); config.Channels.Feishu.Enabled = true; break;
            case "dingtalk": config.Channels.DingTalk ??= new DingTalkConfig(); config.Channels.DingTalk.Enabled = true; break;
            case "email": config.Channels.Email ??= new EmailConfig(); config.Channels.Email.Enabled = true; break;
            case "qq": config.Channels.QQ ??= new QQConfig(); config.Channels.QQ.Enabled = true; break;
            case "matrix": config.Channels.Matrix ??= new MatrixConfig(); config.Channels.Matrix.Enabled = true; break;
        }
    }

    /// <summary>
    /// Disables a channel in the config.
    /// </summary>
    public static void DisableChannel(AgentConfig config, string channelId)
    {
        config.Channels ??= new ChannelsConfig();
        switch (channelId.ToLower())
        {
            case "telegram": if (config.Channels.Telegram != null) config.Channels.Telegram.Enabled = false; break;
            case "discord": if (config.Channels.Discord != null) config.Channels.Discord.Enabled = false; break;
            case "whatsapp": if (config.Channels.WhatsApp != null) config.Channels.WhatsApp.Enabled = false; break;
            case "slack": if (config.Channels.Slack != null) config.Channels.Slack.Enabled = false; break;
            case "feishu": if (config.Channels.Feishu != null) config.Channels.Feishu.Enabled = false; break;
            case "dingtalk": if (config.Channels.DingTalk != null) config.Channels.DingTalk.Enabled = false; break;
            case "email": if (config.Channels.Email != null) config.Channels.Email.Enabled = false; break;
            case "qq": if (config.Channels.QQ != null) config.Channels.QQ.Enabled = false; break;
            case "matrix": if (config.Channels.Matrix != null) config.Channels.Matrix.Enabled = false; break;
        }
    }

    /// <summary>
    /// Gets the editable channel config object for the given channel ID,
    /// paired with a <see cref="ChannelConfigRef"/> that holds a mutable reference
    /// so the component's field is updated when <paramref name="onChanged"/> fires.
    /// </summary>
    public static (ChannelConfigRef ConfigRef, Action<object> OnChanged) GetChannelEditor(
        AgentConfig config, string channelId)
    {
        config.Channels ??= new ChannelsConfig();
        return channelId.ToLower() switch
        {
            "telegram" => CreateRef<TelegramConfig>(() => config.Channels!.Telegram, (v) => config.Channels!.Telegram = v),
            "discord" => CreateRef<DiscordConfig>(() => config.Channels!.Discord, (v) => config.Channels!.Discord = v),
            "whatsapp" => CreateRef<WhatsAppConfig>(() => config.Channels!.WhatsApp, (v) => config.Channels!.WhatsApp = v),
            "slack" => CreateRef<SlackConfig>(() => config.Channels!.Slack, (v) => config.Channels!.Slack = v),
            "feishu" => CreateRef<FeishuConfig>(() => config.Channels!.Feishu, (v) => config.Channels!.Feishu = v),
            "dingtalk" => CreateRef<DingTalkConfig>(() => config.Channels!.DingTalk, (v) => config.Channels!.DingTalk = v),
            "email" => CreateRef<EmailConfig>(() => config.Channels!.Email, (v) => config.Channels!.Email = v),
            "qq" => CreateRef<QQConfig>(() => config.Channels!.QQ, (v) => config.Channels!.QQ = v),
            "matrix" => CreateRef<MatrixConfig>(() => config.Channels!.Matrix, (v) => config.Channels!.Matrix = v),
            "mo" or "mochat" => CreateRef<MochatConfig>(() => config.Channels!.Mochat, (v) => config.Channels!.Mochat = v),
            _ => throw new ArgumentException($"Unknown channel: {channelId}")
        };
    }

    /// <summary>
    /// Creates a <see cref="ChannelConfigRef"/> with a getter/setter pair so that
    /// <paramref name="setter"/> writes back to the source field when the editor fires.
    /// </summary>
    private static (ChannelConfigRef, Action<object>) CreateRef<T>(
        Func<T?> getter,
        Action<T> setter) where T : class, new()
    {
        var current = getter() ?? new T();
        var ref_ = new ChannelConfigRef(current);
        return (ref_, obj =>
        {
            if (obj is T t)
            {
                current = t;
                setter(t);
                ref_.Config = t;
            }
        });
    }

    /// <summary>
    /// Sets the channel config object from the given channel ID.
    /// </summary>
    public static void SetChannelConfig(AgentConfig config, string channelId, object configObj)
    {
        config.Channels ??= new ChannelsConfig();
        switch (channelId.ToLower())
        {
            case "telegram": config.Channels.Telegram = (TelegramConfig)configObj; break;
            case "discord": config.Channels.Discord = (DiscordConfig)configObj; break;
            case "whatsapp": config.Channels.WhatsApp = (WhatsAppConfig)configObj; break;
            case "slack": config.Channels.Slack = (SlackConfig)configObj; break;
            case "feishu": config.Channels.Feishu = (FeishuConfig)configObj; break;
            case "dingtalk": config.Channels.DingTalk = (DingTalkConfig)configObj; break;
            case "email": config.Channels.Email = (EmailConfig)configObj; break;
            case "qq": config.Channels.QQ = (QQConfig)configObj; break;
            case "matrix": config.Channels.Matrix = (MatrixConfig)configObj; break;
            case "mo":
            case "mochat": config.Channels.Mochat = (MochatConfig)configObj; break;
        }
    }

    /// <summary>
    /// Gets the channel-specific config object for the given channel ID.
    /// </summary>
    public static object? GetChannelConfig(AgentConfig config, string channelId)
    {
        config.Channels ??= new ChannelsConfig();
        return channelId.ToLower() switch
        {
            "telegram" => config.Channels.Telegram ??= new TelegramConfig { Enabled = true },
            "discord" => config.Channels.Discord ??= new DiscordConfig { Enabled = true },
            "whatsapp" => config.Channels.WhatsApp ??= new WhatsAppConfig { Enabled = true },
            "slack" => config.Channels.Slack ??= new SlackConfig { Enabled = true },
            "feishu" => config.Channels.Feishu ??= new FeishuConfig { Enabled = true },
            "dingtalk" => config.Channels.DingTalk ??= new DingTalkConfig { Enabled = true },
            "email" => config.Channels.Email ??= new EmailConfig { Enabled = true },
            "qq" => config.Channels.QQ ??= new QQConfig { Enabled = true },
            "matrix" => config.Channels.Matrix ??= new MatrixConfig { Enabled = true },
            "mo" or "mochat" => config.Channels.Mochat ??= new MochatConfig { Enabled = true },
            _ => null
        };
    }

    /// <summary>
    /// Gets the channel-specific validation message for connection testing.
    /// Returns null if validation passes.
    /// </summary>
    public static string? ValidateChannelConfig(object configObj, string channelId)
    {
        return (channelId.ToLower(), configObj) switch
        {
            ("telegram", TelegramConfig c) when string.IsNullOrEmpty(c.Token) => "请先配置 Bot Token",
            ("discord", DiscordConfig c) when string.IsNullOrEmpty(c.Token) => "请先配置 Bot Token",
            ("whatsapp", WhatsAppConfig c) when string.IsNullOrEmpty(c.BridgeUrl) => "请先配置 Bridge URL",
            ("slack", SlackConfig c) when string.IsNullOrEmpty(c.BotToken) => "请先配置 Bot Token",
            ("feishu", FeishuConfig c) when string.IsNullOrEmpty(c.AppId) || string.IsNullOrEmpty(c.AppSecret) => "请先配置 App ID 和 App Secret",
            ("dingtalk", DingTalkConfig c) when string.IsNullOrEmpty(c.ClientId) || string.IsNullOrEmpty(c.ClientSecret) => "请先配置 Client ID 和 Client Secret",
            ("email", EmailConfig c) when string.IsNullOrEmpty(c.ImapHost) || string.IsNullOrEmpty(c.SmtpHost) => "请先配置 IMAP 和 SMTP 服务器",
            ("qq", QQConfig c) when string.IsNullOrEmpty(c.AppId) || string.IsNullOrEmpty(c.Secret) => "请先配置 App ID 和 Secret",
            ("matrix", MatrixConfig c) when string.IsNullOrEmpty(c.AccessToken) || string.IsNullOrEmpty(c.Homeserver) => "请先配置 Homeserver 和 AccessToken",
            ("mo" or "mochat", MochatConfig c) when string.IsNullOrEmpty(c.ClawToken) => "请先配置 Claw Token",
            _ => null
        };
    }
}
