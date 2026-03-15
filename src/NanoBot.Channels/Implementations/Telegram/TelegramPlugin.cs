using Microsoft.Extensions.Logging;
using NanoBot.Channels.Abstractions;
using NanoBot.Core.Bus;
using NanoBot.Core.Channels;
using NanoBot.Core.Channels.Accounts;
using NanoBot.Core.Channels.Adapters;
using NanoBot.Core.Configuration;

namespace NanoBot.Channels.Implementations.Telegram;

/// <summary>
/// Telegram account configuration.
/// </summary>
public class TelegramAccount
{
    public string AccountId { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public List<string> AllowFrom { get; set; } = new();
    public string? Proxy { get; set; }
    public bool ReplyToMessage { get; set; }
}

/// <summary>
/// Telegram channel plugin implementation demonstrating the IChannelPlugin interface.
/// This shows how existing channels can be adapted to the new plugin architecture.
/// </summary>
public class TelegramPlugin : IChannelPlugin<TelegramAccount>
{
    private readonly TelegramChannel _channel;
    
    public TelegramPlugin(TelegramChannel channel)
    {
        _channel = channel;
    }
    
    public ChannelId Id => "telegram";
    
    public ChannelPluginMeta Meta => new(
        Id: "telegram",
        Name: "Telegram",
        Description: "Telegram Bot API channel",
        Version: "1.0.0",
        SupportedMessageTypes: new[] { "text", "markdown", "media" }
    );
    
    public ChannelCapabilities Capabilities => new()
    {
        SupportsDirectMessages = true,
        SupportsGroups = true,
        SupportsMedia = true,
        SupportsReplies = true,
        SupportsTypingIndicator = true,
        SupportsInlineKeyboard = true,
        MaxMessageLength = 4096,
        SupportedContentTypes = new[] { "text", "markdown", "html" }
    };
    
    public IChannelConfigAdapter<TelegramAccount> Config => new TelegramConfigAdapter(_channel);
    
    public IChannelSecurityAdapter<TelegramAccount>? Security => new TelegramSecurityAdapter();
    
    public IChannelOutboundAdapter? Outbound => new TelegramOutboundAdapter(_channel);
    
    public IChannelGroupAdapter? Groups => null;  // Not implemented
    
    public IChannelMentionAdapter? Mentions => null;  // Not implemented
    
    public IChannelThreadingAdapter? Threading => null;  // Not implemented
    
    public IChannelStreamingAdapter? Streaming => null;  // Not implemented
    
    public IChannelHeartbeatAdapter? Heartbeat => null;  // Not implemented
}

/// <summary>
/// Configuration adapter for Telegram accounts.
/// </summary>
public class TelegramConfigAdapter : IChannelConfigAdapter<TelegramAccount>
{
    private readonly TelegramChannel _channel;
    private readonly TelegramConfig _config;
    
    public TelegramConfigAdapter(TelegramChannel channel)
    {
        _channel = channel;
        _config = new TelegramConfig();  // Would come from configuration service
    }
    
    public Task<TelegramAccount?> GetAccountAsync(string accountId, CancellationToken cancellationToken = default)
    {
        // Single account for now - multi-account would parse from config
        var account = new TelegramAccount
        {
            AccountId = "default",
            AccountName = "Default Bot",
            Token = _config.Token,
            AllowFrom = _config.AllowFrom?.ToList() ?? new List<string>(),
            Proxy = _config.Proxy,
            ReplyToMessage = _config.ReplyToMessage
        };
        
        return Task.FromResult<TelegramAccount?>(account);
    }
    
    public Task<IReadOnlyList<TelegramAccount>> GetAccountsAsync(CancellationToken cancellationToken = default)
    {
        var accounts = new List<TelegramAccount>();
        
        if (!string.IsNullOrEmpty(_config.Token))
        {
            accounts.Add(new TelegramAccount
            {
                AccountId = "default",
                AccountName = "Default Bot",
                Token = _config.Token,
                AllowFrom = _config.AllowFrom?.ToList() ?? new List<string>()
            });
        }
        
        return Task.FromResult<IReadOnlyList<TelegramAccount>>(accounts);
    }
    
    public Task<bool> ValidateAccountAsync(TelegramAccount account, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(!string.IsNullOrEmpty(account.Token));
    }
}

/// <summary>
/// Security adapter for Telegram.
/// </summary>
public class TelegramSecurityAdapter : IChannelSecurityAdapter<TelegramAccount>
{
    public Task<bool> IsAllowedAsync(TelegramAccount account, InboundMessage message, CancellationToken cancellationToken = default)
    {
        if (account.AllowFrom.Count == 0)
            return Task.FromResult(true);  // Allow all by default
        
        if (account.AllowFrom.Contains("*"))
            return Task.FromResult(true);
        
        if (account.AllowFrom.Contains(message.SenderId))
            return Task.FromResult(true);
        
        return Task.FromResult(false);
    }
    
    public Task<bool> IsGroupAdminAsync(TelegramAccount account, string groupId, string userId, CancellationToken cancellationToken = default)
    {
        // Would need to query Telegram API for admin status
        return Task.FromResult(false);
    }
    
    public IReadOnlyList<string> GetAllowedSenders(TelegramAccount account)
    {
        return account.AllowFrom;
    }
}

/// <summary>
/// Outbound adapter for Telegram.
/// </summary>
public class TelegramOutboundAdapter : IChannelOutboundAdapter
{
    private readonly TelegramChannel _channel;
    
    public TelegramOutboundAdapter(TelegramChannel channel)
    {
        _channel = channel;
    }
    
    public async Task SendMessageAsync(string chatId, OutboundMessage message, CancellationToken cancellationToken = default)
    {
        // Forward to the channel's send method
        await _channel.SendMessageAsync(message, cancellationToken);
    }
    
    public Task SendMediaAsync(string chatId, string mediaPath, string? caption = null, CancellationToken cancellationToken = default)
    {
        // Would implement media sending
        return Task.CompletedTask;
    }
    
    public Task SendTypingIndicatorAsync(string chatId, CancellationToken cancellationToken = default)
    {
        // Would implement typing indicator
        return Task.CompletedTask;
    }
}
