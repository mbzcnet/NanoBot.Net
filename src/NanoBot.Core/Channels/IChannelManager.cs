using NanoBot.Core.Bus;

namespace NanoBot.Core.Channels;

public interface IChannelManager
{
    void Register(IChannel channel);
    IChannel? GetChannel(string id);
    IReadOnlyList<IChannel> GetChannelsByType(string type);
    IReadOnlyList<IChannel> GetAllChannels();
    Task StartAllAsync(CancellationToken cancellationToken = default);
    Task StopAllAsync(CancellationToken cancellationToken = default);

    event EventHandler<InboundMessage>? MessageReceived;

    IReadOnlyList<string> EnabledChannels { get; }
    IDictionary<string, ChannelStatus> GetStatus();
}
