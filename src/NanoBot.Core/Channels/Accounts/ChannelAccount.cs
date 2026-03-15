namespace NanoBot.Core.Channels.Accounts;

/// <summary>
/// Represents a channel account configuration.
/// </summary>
public class ChannelAccount
{
    /// <summary>
    /// Unique identifier for this account.
    /// </summary>
    public string AccountId { get; set; } = string.Empty;
    
    /// <summary>
    /// Human-readable name for this account.
    /// </summary>
    public string AccountName { get; set; } = string.Empty;
    
    /// <summary>
    /// Account-specific configuration (JSON).
    /// </summary>
    public Dictionary<string, object?> Config { get; set; } = new();
    
    /// <summary>
    /// Current status of this account.
    /// </summary>
    public AccountStatus Status { get; set; } = AccountStatus.Inactive;
    
    /// <summary>
    /// Last active timestamp.
    /// </summary>
    public DateTimeOffset LastActive { get; set; } = DateTimeOffset.UtcNow;
    
    /// <summary>
    /// Error message if status is Error.
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Account status enumeration.
/// </summary>
public enum AccountStatus
{
    /// <summary>
    /// Account is active and connected.
    /// </summary>
    Active,
    
    /// <summary>
    /// Account is not active.
    /// </summary>
    Inactive,
    
    /// <summary>
    /// Account encountered an error.
    /// </summary>
    Error,
    
    /// <summary>
    /// Account is currently connecting.
    /// </summary>
    Connecting,
    
    /// <summary>
    /// Account is disconnected.
    /// </summary>
    Disconnected
}

/// <summary>
/// Account configuration for deserialization from config files.
/// </summary>
public class ChannelAccountConfig
{
    /// <summary>
    /// Unique identifier for this account.
    /// </summary>
    public string Id { get; set; } = string.Empty;
    
    /// <summary>
    /// Human-readable name for this account.
    /// </summary>
    public string? Name { get; set; }
    
    /// <summary>
    /// Whether this account is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
