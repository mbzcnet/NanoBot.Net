using System.Collections.Generic;
using NanoBot.Core.Bus;

namespace NanoBot.Core.Channels;

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
