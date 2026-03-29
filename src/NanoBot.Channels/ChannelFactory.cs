using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NanoBot.Channels.Abstractions;
using NanoBot.Channels.Implementations.DingTalk;
using NanoBot.Channels.Implementations.Discord;
using NanoBot.Channels.Implementations.Email;
using NanoBot.Channels.Implementations.Feishu;
using NanoBot.Channels.Implementations.Mochat;
using NanoBot.Channels.Implementations.QQ;
using NanoBot.Channels.Implementations.Slack;
using NanoBot.Channels.Implementations.Telegram;
using NanoBot.Channels.Implementations.WeiXin;
using NanoBot.Channels.Implementations.WhatsApp;
using NanoBot.Core.Bus;
using NanoBot.Core.Channels;
using NanoBot.Core.Configuration;

namespace NanoBot.Channels;

/// <summary>
/// Interface for the channel factory that creates and registers channel instances.
/// </summary>
public interface IChannelFactory
{
    void CreateAndRegisterAll(
        IChannelManager channelManager,
        ConcurrentDictionary<string, IChannel> channelCache);
}

/// <summary>
/// Creates and configures channel instances based on ChannelsConfig.
/// This bridges the gap between configuration and the ChannelManager registration model.
/// </summary>
public sealed class ChannelFactory : IChannelFactory
{
    private readonly IMessageBus _bus;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ChannelsConfig _config;

    public ChannelFactory(
        IMessageBus bus,
        ILoggerFactory loggerFactory,
        ChannelsConfig config)
    {
        _bus = bus;
        _loggerFactory = loggerFactory;
        _config = config;
    }

    /// <summary>
    /// Creates all enabled channels from configuration and registers them with the manager.
    /// </summary>
    public void CreateAndRegisterAll(
        IChannelManager channelManager,
        ConcurrentDictionary<string, IChannel> channelCache)
    {
        // Telegram
        if (_config.Telegram?.Enabled == true)
        {
            var channel = CreateChannel<TelegramChannel, TelegramConfig>(
                _config.Telegram,
                static (cfg, bus, logger) => new TelegramChannel(cfg, bus, logger));
            if (channel != null) Register(channel, channelManager, channelCache);
        }

        // Discord
        if (_config.Discord?.Enabled == true)
        {
            var channel = CreateChannel<DiscordChannel, DiscordConfig>(
                _config.Discord,
                static (cfg, bus, logger) => new DiscordChannel(cfg, bus, logger));
            if (channel != null) Register(channel, channelManager, channelCache);
        }

        // Feishu
        if (_config.Feishu?.Enabled == true)
        {
            var channel = CreateChannel<FeishuChannel, FeishuConfig>(
                _config.Feishu,
                static (cfg, bus, logger) => new FeishuChannel(cfg, bus, logger));
            if (channel != null) Register(channel, channelManager, channelCache);
        }

        // WhatsApp
        if (_config.WhatsApp?.Enabled == true)
        {
            var channel = CreateChannel<WhatsAppChannel, WhatsAppConfig>(
                _config.WhatsApp,
                static (cfg, bus, logger) => new WhatsAppChannel(cfg, bus, logger));
            if (channel != null) Register(channel, channelManager, channelCache);
        }

        // DingTalk
        if (_config.DingTalk?.Enabled == true)
        {
            var channel = CreateChannel<DingTalkChannel, DingTalkConfig>(
                _config.DingTalk,
                static (cfg, bus, logger) => new DingTalkChannel(cfg, bus, logger));
            if (channel != null) Register(channel, channelManager, channelCache);
        }

        // Email
        if (_config.Email?.Enabled == true)
        {
            var channel = CreateChannel<EmailChannel, EmailConfig>(
                _config.Email,
                static (cfg, bus, logger) => new EmailChannel(cfg, bus, logger));
            if (channel != null) Register(channel, channelManager, channelCache);
        }

        // Slack
        if (_config.Slack?.Enabled == true)
        {
            var channel = CreateChannel<SlackChannel, SlackConfig>(
                _config.Slack,
                static (cfg, bus, logger) => new SlackChannel(cfg, bus, logger));
            if (channel != null) Register(channel, channelManager, channelCache);
        }

        // QQ
        if (_config.QQ?.Enabled == true)
        {
            var channel = CreateChannel<QQChannel, QQConfig>(
                _config.QQ,
                static (cfg, bus, logger) => new QQChannel(cfg, bus, logger));
            if (channel != null) Register(channel, channelManager, channelCache);
        }

        // Mochat
        if (_config.Mochat?.Enabled == true)
        {
            var channel = CreateChannel<MochatChannel, MochatConfig>(
                _config.Mochat,
                static (cfg, bus, logger) => new MochatChannel(cfg, bus, logger));
            if (channel != null) Register(channel, channelManager, channelCache);
        }

        // WeiXin
        if (_config.WeiXin?.Enabled == true)
        {
            var channel = CreateChannel<WeiXinChannel, WeiXinConfig>(
                _config.WeiXin,
                static (cfg, bus, logger) => new WeiXinChannel(cfg, bus, logger));
            if (channel != null) Register(channel, channelManager, channelCache);
        }
    }

    private void Register(
        IChannel channel,
        IChannelManager channelManager,
        ConcurrentDictionary<string, IChannel> channelCache)
    {
        channelManager.Register(channel);
        channelCache[channel.Id] = channel;
        var logger = _loggerFactory.CreateLogger<ChannelFactory>();
        logger.LogInformation("Channel registered: {ChannelId}", channel.Id);
    }

    private TChannel? CreateChannel<TChannel, TConfig>(
        TConfig config,
        Func<TConfig, IMessageBus, ILogger<TChannel>, TChannel> factory)
        where TChannel : class, IChannel
        where TConfig : class
    {
        try
        {
            var logger = _loggerFactory.CreateLogger<TChannel>();
            return factory(config, _bus, logger);
        }
        catch (Exception ex)
        {
            var logger = _loggerFactory.CreateLogger<ChannelFactory>();
            logger.LogWarning(ex, "Failed to create channel {ChannelType}", typeof(TChannel).Name);
            return null;
        }
    }
}
