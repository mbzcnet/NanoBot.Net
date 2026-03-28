using NanoBot.Core.Channels;
using NanoBot.Core.Configuration;

namespace NanoBot.Core.Services;

/// <summary>
/// Service for managing channel configurations.
/// </summary>
public interface IChannelConfigService
{
    /// <summary>
    /// Gets the current channels configuration.
    /// </summary>
    Task<ChannelsConfig?> GetChannelsConfigAsync();

    /// <summary>
    /// Saves the channels configuration.
    /// </summary>
    Task SaveChannelsConfigAsync(ChannelsConfig config);

    /// <summary>
    /// Checks if a channel is enabled.
    /// </summary>
    Task<bool> IsChannelEnabledAsync(string channelId);

    /// <summary>
    /// Enables a channel.
    /// </summary>
    Task EnableChannelAsync(string channelId);

    /// <summary>
    /// Disables a channel.
    /// </summary>
    Task DisableChannelAsync(string channelId);

    /// <summary>
    /// Gets the configuration for a specific channel.
    /// </summary>
    Task<T?> GetChannelConfigAsync<T>(string channelId) where T : class, new();

    /// <summary>
    /// Updates the configuration for a specific channel.
    /// </summary>
    Task UpdateChannelConfigAsync<T>(string channelId, T config) where T : class;
}
