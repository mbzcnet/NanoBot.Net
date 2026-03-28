using System.Collections.Generic;
using NanoBot.Core.Bus;

namespace NanoBot.Core.Channels;

/// <summary>
/// Legacy channel interface - use <see cref="IChannelPlugin{TAccount}"/> instead.
/// </summary>
/// <remarks>
/// This interface is deprecated and will be removed in a future version.
/// New channel implementations should use <see cref="IChannelPlugin{TAccount}"/>.
/// </remarks>
[Obsolete("Use IChannelPlugin<TAccount> instead. This interface will be removed in a future version.")]
public interface IChannel
{
    string Id { get; }
    string Type { get; }
    bool IsConnected { get; }

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task SendMessageAsync(OutboundMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the default configuration for this channel.
    /// Used by onboard to auto-populate config.json with default values.
    /// </summary>
    /// <returns>Dictionary of default config values, or null if no defaults.</returns>
    IDictionary<string, object?>? DefaultConfig();

    event EventHandler<InboundMessage>? MessageReceived;
}
