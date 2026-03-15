namespace NanoBot.Core.Channels;

/// <summary>
/// Represents a unique channel identifier.
/// </summary>
public readonly record struct ChannelId(string Value)
{
    public static ChannelId Empty => new(string.Empty);
    
    public override string ToString() => Value;
    
    public static implicit operator string(ChannelId id) => id.Value;
    public static implicit operator ChannelId(string value) => new(value);
}

/// <summary>
/// Defines the capabilities of a channel implementation.
/// </summary>
public class ChannelCapabilities
{
    /// <summary>
    /// Supports direct messages between users.
    /// </summary>
    public bool SupportsDirectMessages { get; init; } = true;
    
    /// <summary>
    /// Supports group/chat rooms with multiple participants.
    /// </summary>
    public bool SupportsGroups { get; init; } = true;
    
    /// <summary>
    /// Supports sending and receiving media (images, videos, files).
    /// </summary>
    public bool SupportsMedia { get; init; } = true;
    
    /// <summary>
    /// Supports streaming responses (real-time token-by-token delivery).
    /// </summary>
    public bool SupportsStreaming { get; init; }
    
    /// <summary>
    /// Supports threaded conversations.
    /// </summary>
    public bool SupportsThreading { get; init; }
    
    /// <summary>
    /// Supports @mentions of users.
    /// </summary>
    public bool SupportsMentions { get; init; }
    
    /// <summary>
    /// Supports message reactions (emoji, etc).
    /// </summary>
    public bool SupportsReactions { get; init; }
    
    /// <summary>
    /// Supports multiple account instances per channel type.
    /// </summary>
    public bool SupportsMultiAccount { get; init; }
    
    /// <summary>
    /// Supports message replies.
    /// </summary>
    public bool SupportsReplies { get; init; } = true;
    
    /// <summary>
    /// Supports editing sent messages.
    /// </summary>
    public bool SupportsEdit { get; init; }
    
    /// <summary>
    /// Supports deleting messages.
    /// </summary>
    public bool SupportsDelete { get; init; }
    
    /// <summary>
    /// Supports typing indicators.
    /// </summary>
    public bool SupportsTypingIndicator { get; init; } = true;
    
    /// <summary>
    /// Supports sending inline keyboards/buttons.
    /// </summary>
    public bool SupportsInlineKeyboard { get; init; }
    
    /// <summary>
    /// Maximum message length supported.
    /// </summary>
    public int? MaxMessageLength { get; init; }
    
    /// <summary>
    /// Supported message content types.
    /// </summary>
    public IReadOnlyList<string> SupportedContentTypes { get; init; } = new[] { "text", "markdown" };
}

/// <summary>
/// Channel metadata for plugin system.
/// </summary>
public record ChannelPluginMeta(
    string Id,
    string Name,
    string Description,
    string Version,
    IReadOnlyList<string> SupportedMessageTypes);

/// <summary>
/// Message source context.
/// </summary>
public enum MessageSource
{
    DirectMessage,
    Group,
    Channel,
    Unknown
}
