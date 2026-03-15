using NanoBot.Core.Channels.Adapters;
using NanoBot.Core.Channels.Accounts;

namespace NanoBot.Core.Channels;

/// <summary>
/// Channel plugin interface - the core abstraction for channel implementations.
/// This is the main entry point for channel plugins, similar to openclaw's ChannelPlugin<T>.
/// </summary>
public interface IChannelPlugin<TAccount> where TAccount : class
{
    /// <summary>
    /// Unique identifier for this channel plugin.
    /// </summary>
    ChannelId Id { get; }
    
    /// <summary>
    /// Metadata about the channel plugin.
    /// </summary>
    ChannelPluginMeta Meta { get; }
    
    /// <summary>
    /// Capabilities supported by this channel.
    /// </summary>
    ChannelCapabilities Capabilities { get; }
    
    /// <summary>
    /// Configuration adapter for managing accounts.
    /// </summary>
    IChannelConfigAdapter<TAccount> Config { get; }
    
    /// <summary>
    /// Security adapter for access control (optional).
    /// </summary>
    IChannelSecurityAdapter<TAccount>? Security { get; }
    
    /// <summary>
    /// Outbound message adapter (optional).
    /// </summary>
    IChannelOutboundAdapter? Outbound { get; }
    
    /// <summary>
    /// Group management adapter (optional).
    /// </summary>
    IChannelGroupAdapter? Groups { get; }
    
    /// <summary>
    /// Mention handling adapter (optional).
    /// </summary>
    IChannelMentionAdapter? Mentions { get; }
    
    /// <summary>
    /// Threading adapter (optional).
    /// </summary>
    IChannelThreadingAdapter? Threading { get; }
    
    /// <summary>
    /// Streaming adapter (optional).
    /// </summary>
    IChannelStreamingAdapter? Streaming { get; }
    
    /// <summary>
    /// Heartbeat/health check adapter (optional).
    /// </summary>
    IChannelHeartbeatAdapter? Heartbeat { get; }
}

/// <summary>
/// Marker interface for channel implementations that support multiple accounts.
/// </summary>
public interface IMultiAccountChannel
{
    /// <summary>
    /// Gets all configured accounts.
    /// </summary>
    Task<IReadOnlyList<ChannelAccount>> GetAccountsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets an account by ID.
    /// </summary>
    Task<ChannelAccount?> GetAccountAsync(string accountId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Adds a new account.
    /// </summary>
    Task AddAccountAsync(ChannelAccount account, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Removes an account.
    /// </summary>
    Task RemoveAccountAsync(string accountId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Updates account status.
    /// </summary>
    Task UpdateAccountStatusAsync(string accountId, AccountStatus status, CancellationToken cancellationToken = default);
}
