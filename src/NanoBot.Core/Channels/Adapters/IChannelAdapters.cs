using NanoBot.Core.Bus;

namespace NanoBot.Core.Channels.Adapters;

/// <summary>
/// Configuration adapter for channel accounts.
/// </summary>
public interface IChannelConfigAdapter<TAccount> where TAccount : class
{
    /// <summary>
    /// Resolves an account by ID.
    /// </summary>
    Task<TAccount?> GetAccountAsync(string accountId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Lists all configured accounts.
    /// </summary>
    Task<IReadOnlyList<TAccount>> GetAccountsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Validates account configuration.
    /// </summary>
    Task<bool> ValidateAccountAsync(TAccount account, CancellationToken cancellationToken = default);
}

/// <summary>
/// Security adapter for access control.
/// </summary>
public interface IChannelSecurityAdapter<TAccount> where TAccount : class
{
    /// <summary>
    /// Checks if a sender is allowed to interact with the channel.
    /// </summary>
    Task<bool> IsAllowedAsync(TAccount account, InboundMessage message, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks if a user is an admin of a group.
    /// </summary>
    Task<bool> IsGroupAdminAsync(TAccount account, string groupId, string userId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the list of allowed sender IDs.
    /// </summary>
    IReadOnlyList<string> GetAllowedSenders(TAccount account);
}

/// <summary>
/// Outbound message adapter for sending messages.
/// </summary>
public interface IChannelOutboundAdapter
{
    /// <summary>
    /// Sends a text message.
    /// </summary>
    Task SendMessageAsync(string chatId, OutboundMessage message, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Sends media content.
    /// </summary>
    Task SendMediaAsync(string chatId, string mediaPath, string? caption = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Sends a typing indicator.
    /// </summary>
    Task SendTypingIndicatorAsync(string chatId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Group management adapter.
/// </summary>
public interface IChannelGroupAdapter
{
    /// <summary>
    /// Gets all groups the bot is member of.
    /// </summary>
    Task<IReadOnlyList<GroupInfo>> GetGroupsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets group information by ID.
    /// </summary>
    Task<GroupInfo?> GetGroupAsync(string groupId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Invites a user to a group.
    /// </summary>
    Task InviteMemberAsync(string groupId, string userId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Removes a member from a group.
    /// </summary>
    Task RemoveMemberAsync(string groupId, string userId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Sets a user as admin in a group.
    /// </summary>
    Task SetGroupAdminAsync(string groupId, string userId, bool isAdmin, CancellationToken cancellationToken = default);
}

/// <summary>
/// Group information model.
/// </summary>
public class GroupInfo
{
    public string GroupId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int MemberCount { get; set; }
    public bool IsBotMember { get; set; }
    public string? InviteLink { get; set; }
}

/// <summary>
/// Mention handling adapter.
/// </summary>
public interface IChannelMentionAdapter
{
    /// <summary>
    /// Parses mentions from message content.
    /// </summary>
    Task<IReadOnlyList<UserInfo>> ParseMentionsAsync(string content, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Formats a mention for a user.
    /// </summary>
    Task<string> FormatMentionAsync(string userId, string displayName, CancellationToken cancellationToken = default);
}

/// <summary>
/// User information model.
/// </summary>
public class UserInfo
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string? AvatarUrl { get; set; }
}

/// <summary>
/// Threading adapter for reply threads.
/// </summary>
public interface IChannelThreadingAdapter
{
    /// <summary>
    /// Creates a new thread.
    /// </summary>
    Task<string> CreateThreadAsync(string parentMessageId, string initialContent, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Replies to an existing thread.
    /// </summary>
    Task ReplyToThreadAsync(string threadId, string content, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets messages in a thread.
    /// </summary>
    Task<IReadOnlyList<InboundMessage>> GetThreadMessagesAsync(string threadId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Streaming adapter for real-time message delivery.
/// </summary>
public interface IChannelStreamingAdapter
{
    /// <summary>
    /// Streams a message content token by token.
    /// </summary>
    IAsyncEnumerable<string> StreamMessageAsync(string chatId, string content, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Finalizes a streaming message.
    /// </summary>
    Task FinalizeStreamAsync(string chatId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Heartbeat/health check adapter.
/// </summary>
public interface IChannelHeartbeatAdapter
{
    /// <summary>
    /// Checks the health of the channel connection.
    /// </summary>
    Task<HealthStatus> CheckHealthAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Event raised when health status changes.
    /// </summary>
    event EventHandler<HealthChangedEventArgs>? HealthChanged;
}

/// <summary>
/// Health status result.
/// </summary>
public class HealthStatus
{
    public HealthState State { get; set; } = HealthState.Unknown;
    public string? ErrorMessage { get; set; }
    public DateTime LastChecked { get; set; } = DateTime.UtcNow;
    public TimeSpan? Latency { get; set; }
}

/// <summary>
/// Health state enumeration.
/// </summary>
public enum HealthState
{
    Unknown,
    Healthy,
    Degraded,
    Unhealthy
}

/// <summary>
/// Health changed event args.
/// </summary>
public class HealthChangedEventArgs : EventArgs
{
    public required string ChannelId { get; init; }
    public required string AccountId { get; init; }
    public required HealthStatus OldStatus { get; init; }
    public required HealthStatus NewStatus { get; init; }
}
